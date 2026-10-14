from __future__ import annotations

from collections import Counter
from datetime import datetime
import hashlib
import json
import math
from pathlib import Path
import time

from .rules import scan, grouped, validate_profile
from .windows import Desktop, DesktopError, desktop_paths


def plan_layout(snapshot, groups):
    icons = snapshot['icons']
    counts = Counter(icon['name'] for icon in icons)
    file_counts = Counter(item.display for items in groups.values() for item in items)
    lookup = {icon['name']: icon for icon in icons if counts[icon['name']] == 1}
    eligible = {name for name, count in file_counts.items() if count == 1 and name in lookup}
    reserved = [icon['xy'] for icon in icons if icon['name'] not in eligible]
    sx, sy = snapshot['spacing']
    width, height = snapshot['size']
    rows = max(0, (height - 24) // sy)
    columns = max(0, (width - 24) // sx)
    slots = [(16 + x * sx, 16 + y * sy) for x in range(columns) for y in range(rows)]
    slots = [xy for xy in slots if not any(abs(xy[0] - p[0]) < sx and abs(xy[1] - p[1]) < sy for p in reserved)]
    # Categories occupy successive columns, with a breathing row when possible.
    movements, cursor = [], 0
    for category, items in groups.items():
        members = [item for item in items if item.display in eligible]
        for item in members:
            if cursor >= len(slots):
                raise DesktopError('桌面区域不足，未修改图标。请减少图标或改用分类快捷方式')
            icon = lookup[item.display]
            xy = list(slots[cursor])
            cursor += 1
            movements.append({'index': icon['index'], 'name': icon['name'], 'xy': xy, 'category': category})
        if members and rows and cursor + rows < len(slots):
            # Use the next available column, never put a category header over an icon.
            last_x = slots[cursor - 1][0]
            while cursor < len(slots) and slots[cursor][0] == last_x:
                cursor += 1
    return movements, len(icons) - len(eligible)


def digest(path: Path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Organizer:
    def __init__(self, store):
        self.store = store

    def preview(self, profile):
        validate_profile(profile)
        items, warnings = scan(desktop_paths())
        groups = grouped(items, profile)
        return {'groups': {name: [str(item.path) for item in members] for name, members in groups.items()},
                'count': len(items), 'warnings': warnings}

    def organize(self, profile, mode='layout'):
        validate_profile(profile)
        roots = desktop_paths()
        items, warnings = scan(roots)
        groups = grouped(items, profile)
        if not items:
            return '未发现可整理图标'
        if mode == 'shortcuts':
            return self._shortcuts(groups, roots[0])
        with Desktop() as desktop:
            desktop.assert_manual()
            snapshot = desktop.snapshot()
            movements, skipped = plan_layout(snapshot, groups)
            before = {icon['name']: icon['xy'] for icon in snapshot['icons']}
            changed = [move for move in movements if before[move['name']] != move['xy']]
            if not changed:
                return '布局已整齐，无需修改；保留 ' + str(skipped) + ' 个系统/受保护/重名图标'
            snapshot['movable_names'] = [move['name'] for move in movements]
            self.store.backup(snapshot)
            try:
                for move in changed:
                    desktop.move(move['index'], move['name'], move['xy'])
            except Exception as exc:
                # Leave the backup intact for an explicit undo. Never guess identity.
                raise DesktopError('整理中断，备份已保存；点击撤销恢复。' + str(exc)) from exc
        return '整理完成：' + str(len(changed)) + ' 个图标；保留 ' + str(skipped) + ' 个图标' + ('；部分路径无法读取' if warnings else '')

    def _shortcuts(self, groups, root):
        import pythoncom
        import win32com.client
        pythoncom.CoInitialize()
        try:
            output = root / ('桌面分类-' + datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
            if output.exists():
                raise DesktopError('输出目录已存在，请重试')
            # Attempt a layout backup too; the alternate mode must work without Explorer access.
            layout = None
            try:
                with Desktop() as desktop:
                    layout = desktop.snapshot()
            except DesktopError:
                pass
            manifest = {'kind': 'shortcuts', 'output': str(output), 'files': [], 'layout': layout}
            self.store.backup(manifest)
            output.mkdir()
            shell = win32com.client.Dispatch('WScript.Shell')
            for category, items in groups.items():
                # Name-mode groups such as a leading dot are not safe folder names.
                folder_name = category if category not in ('.', '..') else '其他'
                folder = output / folder_name
                folder.mkdir(exist_ok=True)
                for item in items:
                    suffix = hashlib.sha256(str(item.path).encode('utf-8')).hexdigest()[:10]
                    target = folder / (item.path.stem[:100] + '-' + suffix + '.lnk')
                    record = {'path': str(target), 'sha256': None}
                    manifest['files'].append(record)
                    self.store.write_json(self.store.root / 'undo.json', manifest)
                    link = shell.CreateShortcut(str(target))
                    link.TargetPath = str(item.path)
                    link.WorkingDirectory = str(item.path.parent)
                    link.Description = 'DesktopTidy 分类入口；原文件未移动'
                    link.Save()
                    record['sha256'] = digest(target)
                    self.store.write_json(self.store.root / 'undo.json', manifest)
            self.store.write_json(self.store.root / 'backups' / (output.name + '.json'), manifest)
            return '已创建分类快捷方式：' + str(output) + '；原桌面内容保持不变'
        finally:
            pythoncom.CoUninitialize()

    def restore(self, baseline=False):
        snapshot = self.store.load_json('default-layout.json' if baseline else 'undo.json')
        if not snapshot:
            return '暂无默认布局' if baseline else '暂无可撤销的整理'
        if snapshot['kind'] == 'shortcuts':
            return self._undo_shortcuts(snapshot)
        return self._restore_layout(snapshot, baseline)

    def _restore_layout(self, snapshot, baseline):
        with Desktop() as desktop:
            desktop.assert_manual()
            current = desktop.snapshot()
            if snapshot['size'] != current['size'] or snapshot['spacing'] != current['spacing']:
                raise DesktopError('显示器尺寸、缩放或图标间距已变化；为防止图标越界，未恢复旧布局')
            old_counts = Counter(icon['name'] for icon in snapshot['icons'])
            new_counts = Counter(icon['name'] for icon in current['icons'])
            names = set(snapshot.get('movable_names', []))
            # Re-scan safety attributes before restoring; files may have become protected.
            items, _ = scan(desktop_paths())
            safe_counts = Counter(item.display for item in items)
            names &= {name for name, count in safe_counts.items() if count == 1}
            old = {icon['name']: icon for icon in snapshot['icons'] if old_counts[icon['name']] == 1}
            moves = [(icon, old[icon['name']]['xy']) for icon in current['icons']
                     if icon['name'] in names and icon['name'] in old and new_counts[icon['name']] == 1
                     and icon['xy'] != old[icon['name']]['xy']]
            # Newly-created or protected icons must not be overlapped by restoration.
            moving_names = {icon['name'] for icon, _ in moves}
            reserved = [icon['xy'] for icon in current['icons'] if icon['name'] not in moving_names]
            sx, sy = current['spacing']
            if any(abs(xy[0] - p[0]) < sx * .7 and abs(xy[1] - p[1]) < sy * .7 for _, xy in moves for p in reserved):
                raise DesktopError('原位置被新图标占用；未恢复，请先移动占位图标')
            if moves:
                current['movable_names'] = list(moving_names)
                self.store.backup(current)
                for icon, xy in moves:
                    desktop.move(icon['index'], icon['name'], xy)
            return ('默认布局已恢复' if baseline else '撤销成功') + '：' + str(len(moves)) + ' 个图标（失效或受保护图标保留原位）'

    def _undo_shortcuts(self, snapshot):
        output = Path(snapshot['output'])
        user_desktop = desktop_paths()[0].resolve()
        if output.parent.resolve() != user_desktop or not output.name.startswith('桌面分类-') or output.is_symlink():
            raise DesktopError('撤销目录校验失败，未删除任何文件')
        removed, retained = 0, 0
        for record in snapshot['files']:
            path = Path(record['path'])
            if not path.exists():
                continue
            if not path.resolve().is_relative_to(output.resolve()) or path.is_symlink():
                retained += 1
                continue
            if not record['sha256'] or digest(path) != record['sha256']:
                retained += 1
                continue
            path.unlink()  # Only our generated, unchanged shortcut; never a user's source.
            removed += 1
        if output.exists():
            for folder in output.iterdir():
                if folder.is_dir() and not folder.is_symlink():
                    try:
                        folder.rmdir()  # Fails safely when users placed anything inside.
                    except OSError:
                        pass
            try:
                output.rmdir()
            except OSError:
                pass
        # Original positions were never modified in this mode; no Explorer restore needed.
        return '撤销成功：移除 ' + str(removed) + ' 个生成的快捷方式；保留 ' + str(retained) + ' 个已修改项'
