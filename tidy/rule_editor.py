from __future__ import annotations
import copy
from PySide6.QtCore import Qt
from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel, QComboBox, QTableWidget,
    QTableWidgetItem, QInputDialog, QHeaderView)
from .rules import MODES, KINDS, validate_profile
from .theme import ActionButton

class RuleEditor(QDialog):
    def __init__(self, store, parent=None):
        super().__init__(parent)
        self.setWindowTitle('分类规则')
        self.resize(720, 500)
        self.profiles = copy.deepcopy(store.data['profiles'])
        self.selected = store.data['active']
        layout = QVBoxLayout(self)
        layout.setContentsMargins(24, 24, 24, 24)
        row = QHBoxLayout()
        self.selector = QComboBox()
        self.selector.addItems(self.profiles)
        self.selector.setCurrentText(self.selected)
        row.addWidget(self.selector, 1)
        create = ActionButton('另存为新规则')
        create.clicked.connect(self.create)
        row.addWidget(create)
        delete = ActionButton('删除规则集')
        delete.clicked.connect(self.delete)
        row.addWidget(delete)
        layout.addLayout(row)
        self.mode = QComboBox()
        for key, label in MODES.items():
            self.mode.addItem(label, key)
        layout.addWidget(self.mode)
        hint = QLabel('自定义规则按从上到下优先匹配；多个匹配值用逗号分隔。未命中时使用上方分类方式。')
        hint.setObjectName('muted')
        hint.setWordWrap(True)
        layout.addWidget(hint)
        self.table = QTableWidget(0, 3)
        self.table.setHorizontalHeaderLabels(['分类名称', '匹配方式', '匹配值'])
        self.table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        layout.addWidget(self.table, 1)
        tools = QHBoxLayout()
        for text, callback in [('添加规则', self.add_row), ('移除选中', self.remove_row), ('上移', lambda: self.reorder(-1)), ('下移', lambda: self.reorder(1))]:
            button = ActionButton(text)
            button.clicked.connect(callback)
            tools.addWidget(button)
        layout.addLayout(tools)
        self.feedback = QLabel('')
        self.feedback.setObjectName('muted')
        self.feedback.setWordWrap(True)
        layout.addWidget(self.feedback)
        buttons = QHBoxLayout()
        buttons.addStretch()
        cancel = ActionButton('取消')
        cancel.clicked.connect(self.reject)
        save = ActionButton('保存规则', True)
        save.clicked.connect(self.save)
        buttons.addWidget(cancel)
        buttons.addWidget(save)
        layout.addLayout(buttons)
        self.load()
        self.selector.currentTextChanged.connect(self.switch)

    def read(self):
        rules = []
        for row in range(self.table.rowCount()):
            category = self.table.item(row, 0)
            value = self.table.item(row, 2)
            rules.append({'category': category.text().strip() if category else '',
                          'kind': self.table.cellWidget(row, 1).currentData(),
                          'value': value.text().strip() if value else ''})
        return {'mode': self.mode.currentData(), 'rules': rules}

    def load(self):
        profile = self.profiles[self.selected]
        self.mode.setCurrentIndex(self.mode.findData(profile['mode']))
        self.table.setRowCount(0)
        for rule in profile['rules']:
            self.add_row(rule)

    def switch(self, name):
        if not name:
            return
        self.profiles[self.selected] = self.read()
        self.selected = name
        self.load()

    def add_row(self, rule=None):
        if not isinstance(rule, dict):
            rule = {'category': '新分类', 'kind': 'keyword', 'value': ''}
        row = self.table.rowCount()
        self.table.insertRow(row)
        self.table.setItem(row, 0, QTableWidgetItem(rule['category']))
        kind = QComboBox()
        for key, label in KINDS.items():
            kind.addItem(label, key)
        kind.setCurrentIndex(kind.findData(rule['kind']))
        self.table.setCellWidget(row, 1, kind)
        self.table.setItem(row, 2, QTableWidgetItem(rule['value']))

    def remove_row(self):
        rows = sorted({item.row() for item in self.table.selectedIndexes()}, reverse=True)
        for row in rows:
            self.table.removeRow(row)

    def reorder(self, direction):
        row = self.table.currentRow()
        target = row + direction
        if row < 0 or not 0 <= target < self.table.rowCount():
            return
        profile = self.read()
        profile['rules'][row], profile['rules'][target] = profile['rules'][target], profile['rules'][row]
        self.profiles[self.selected] = profile
        self.load()
        self.table.selectRow(target)

    def create(self):
        name, ok = QInputDialog.getText(self, '新规则集', '名称')
        name = name.strip()
        if not ok or not name:
            return
        if name in self.profiles:
            self.feedback.setText('该名称已存在')
            return
        self.profiles[self.selected] = self.read()
        self.profiles[name] = copy.deepcopy(self.profiles[self.selected])
        self.selector.addItem(name)
        self.selector.setCurrentText(name)

    def delete(self):
        if len(self.profiles) == 1:
            self.feedback.setText('至少保留一套规则')
            return
        self.selector.blockSignals(True)
        del self.profiles[self.selected]
        self.selector.removeItem(self.selector.currentIndex())
        self.selected = self.selector.currentText()
        self.selector.blockSignals(False)
        self.load()

    def save(self):
        self.profiles[self.selected] = self.read()
        try:
            for profile in self.profiles.values():
                validate_profile(profile)
        except (ValueError, TypeError) as exc:
            self.feedback.setText(str(exc))
            return
        self.accept()
