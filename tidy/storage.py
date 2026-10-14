from __future__ import annotations

import copy
import json
import os
from pathlib import Path
from datetime import datetime

DEFAULT_RULES = {
    '默认类型': {'mode': 'type', 'rules': []},
    '工作与生活': {'mode': 'type', 'rules': [
        {'category': '工作', 'kind': 'keyword', 'value': '工作,项目,报告'},
        {'category': '截图', 'kind': 'prefix', 'value': '截图,Screenshot'},
    ]},
}
DEFAULTS = {
    'version': 1, 'active': '默认类型', 'profiles': DEFAULT_RULES,
    'auto': False, 'interval': 15, 'startup_organize': False,
    'mode': 'layout', 'widgets': {}, 'tasks': [], 'note': '', 'launchers': [],
}


class Store:
    def __init__(self, root: Path | None = None):
        self.root = root or Path(os.environ.get('LOCALAPPDATA', Path.home())) / 'DesktopTidy'
        self.root.mkdir(parents=True, exist_ok=True)
        self.path = self.root / 'settings.json'
        self.warning = ''
        self.data = copy.deepcopy(DEFAULTS)
        if self.path.exists():
            try:
                loaded = json.loads(self.path.read_text(encoding='utf-8'))
                if not isinstance(loaded, dict) or loaded.get('version') != 1:
                    raise ValueError('配置版本不兼容')
                self.data.update(loaded)
                self.validate()
            except (OSError, ValueError, TypeError, KeyError, AttributeError) as exc:
                # Preserve the broken file; use an independent timestamped copy.
                import shutil
                shutil.copy2(self.path, self.root / ('settings-invalid-' + datetime.now().strftime('%Y%m%d-%H%M%S-%f') + '.json'))
                self.data = copy.deepcopy(DEFAULTS)
                self.warning = '配置损坏，已保留原配置并回退默认设置：' + str(exc)

    def validate(self):
        from .rules import validate_profile
        profiles = self.data['profiles']
        if not isinstance(profiles, dict) or not profiles:
            raise ValueError('规则集为空')
        for name, profile in profiles.items():
            if not isinstance(name, str) or not name.strip():
                raise ValueError('规则集名称无效')
            validate_profile(profile)
        if self.data['active'] not in profiles:
            raise ValueError('当前规则不存在')
        if self.data['mode'] not in ('layout', 'shortcuts'):
            raise ValueError('整理模式无效')
        if not isinstance(self.data['interval'], int) or not 1 <= self.data['interval'] <= 1440:
            raise ValueError('间隔必须为 1–1440 分钟')
        for key, kind in [('widgets', dict), ('tasks', list), ('launchers', list), ('note', str)]:
            if not isinstance(self.data[key], kind):
                raise ValueError('组件数据类型无效：' + key)
        for task in self.data['tasks']:
            if not isinstance(task, dict) or not isinstance(task.get('text'), str):
                raise ValueError('待办数据无效')
        for target in self.data['launchers']:
            if not isinstance(target, str):
                raise ValueError('启动项无效')

    def write_json(self, path: Path, data):
        path.parent.mkdir(parents=True, exist_ok=True)
        temp = path.with_suffix('.tmp')
        with temp.open('w', encoding='utf-8') as stream:
            json.dump(data, stream, ensure_ascii=False, indent=2)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp, path)

    def save(self):
        self.write_json(self.path, self.data)

    def load_json(self, name: str):
        path = self.root / name
        return json.loads(path.read_text(encoding='utf-8')) if path.exists() else None

    def backup(self, snapshot):
        stamp = datetime.now().strftime('%Y%m%d-%H%M%S-%f')
        self.write_json(self.root / 'backups' / (stamp + '.json'), snapshot)
        self.write_json(self.root / 'undo.json', snapshot)
        if not (self.root / 'default-layout.json').exists() and snapshot['kind'] == 'layout':
            self.write_json(self.root / 'default-layout.json', snapshot)
