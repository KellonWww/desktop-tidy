from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
import os

TYPES = OrderedDict([
    ('文档', '.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .md .csv .rtf .odt'),
    ('图片', '.png .jpg .jpeg .gif .webp .bmp .svg .ico .heic .tiff'),
    ('视频', '.mp4 .mkv .avi .mov .wmv .webm .m4v'),
    ('安装包', '.exe .msi .msix .appx .msixbundle'),
    ('压缩包', '.zip .rar .7z .tar .gz .bz2 .xz'),
    ('程序快捷方式', '.lnk .url'),
])
MODES = {'type': '类型', 'name': '名称首字', 'created': '创建月份', 'accessed': '访问月份（非真实使用记录）'}
KINDS = {'prefix': '名前缀', 'keyword': '关键词', 'extension': '扩展名', 'folder': '文件夹名'}


@dataclass(frozen=True)
class Item:
    path: Path
    name: str
    folder: bool
    created: float
    accessed: float
    display: str = ''


def validate_profile(profile):
    if not isinstance(profile, dict) or profile.get('mode') not in MODES:
        raise ValueError('分类方式无效')
    rules = profile.get('rules')
    if not isinstance(rules, list) or len(rules) > 200:
        raise ValueError('规则最多 200 条')
    for rule in rules:
        if not isinstance(rule, dict) or rule.get('kind') not in KINDS:
            raise ValueError('规则类型无效')
        if not isinstance(rule.get('category'), str) or not rule['category'].strip():
            raise ValueError('分类名不能为空')
        if not isinstance(rule.get('value'), str) or not rule['value'].strip():
            raise ValueError('匹配值不能为空')
        name = rule['category'].strip()
        if len(name) > 60 or any(c in name for c in '<>:/' + chr(92) + '|?*') or any(ord(c) < 32 for c in name):
            raise ValueError('分类名含非法字符或过长')
        reserved = {'CON', 'PRN', 'AUX', 'NUL'} | {'COM' + str(i) for i in range(1, 10)} | {'LPT' + str(i) for i in range(1, 10)}
        if name.rstrip(' .') != name or name in ('.', '..') or name.split('.')[0].upper() in reserved:
            raise ValueError('分类名不能使用 Windows 保留名或以点/空格结尾')


def classify(item: Item, profile: dict) -> str:
    name = item.name.casefold()
    for rule in profile['rules']:
        values = [part.strip().casefold() for part in rule['value'].replace('，', ',').split(',') if part.strip()]
        kind = rule['kind']
        match = (
            (kind == 'prefix' and any(name.startswith(v) for v in values))
            or (kind == 'keyword' and any(v in name for v in values))
            or (kind == 'extension' and item.path.suffix.casefold() in [v if v.startswith('.') else '.' + v for v in values])
            or (kind == 'folder' and item.folder and any(v in name for v in values))
        )
        if match:
            return rule['category'].strip()
    mode = profile['mode']
    if mode == 'name':
        return name[0].upper() if name else '其他'
    if mode in ('created', 'accessed'):
        return datetime.fromtimestamp(item.created if mode == 'created' else item.accessed).strftime('%Y-%m')
    if item.folder:
        return '文件夹'
    for category, extensions in TYPES.items():
        if item.path.suffix.casefold() in extensions.split():
            return category
    return '其他'


def scan(roots: list[Path], excluded: Path | None = None):
    items, warnings = [], []
    seen = set()
    for root in roots:
        try:
            children = list(root.iterdir())
        except OSError as exc:
            warnings.append(str(root) + '：' + str(exc))
            continue
        for path in children:
            if excluded and path == excluded:
                continue
            try:
                stat = path.stat(follow_symlinks=False)
                # Hidden, system, read-only, and reparse points stay untouched.
                if getattr(stat, 'st_file_attributes', 0) & (0x1 | 0x2 | 0x4 | 0x400):
                    continue
                key = str(path).casefold()
                if key in seen:
                    continue
                seen.add(key)
                display = path.name
                if os.name == 'nt':
                    try:
                        from .windows import display_name
                        display = display_name(path)
                    except OSError:
                        pass
                items.append(Item(path, path.name, path.is_dir(), stat.st_ctime, stat.st_atime, display))
            except OSError as exc:
                warnings.append(path.name + '：' + str(exc))
    return items, warnings


def grouped(items: list[Item], profile: dict):
    groups = OrderedDict()
    for item in sorted(items, key=lambda x: x.name.casefold()):
        groups.setdefault(classify(item, profile), []).append(item)
    return groups
