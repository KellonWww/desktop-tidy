from __future__ import annotations

import os
from pathlib import Path
from PySide6.QtCore import Qt, QEvent, QTimer, QUrl, QPropertyAnimation, QEasingCurve
from PySide6.QtGui import QDesktopServices
from PySide6.QtWidgets import (QWidget, QFrame, QVBoxLayout, QHBoxLayout, QLabel, QMenu,
    QSizeGrip, QApplication, QTextEdit, QLineEdit, QComboBox, QListWidget, QListWidgetItem,
    QFileDialog, QInputDialog)
from .theme import ActionButton


class FloatingWidget(QWidget):
    def __init__(self, key, title, store):
        super().__init__(None, Qt.WindowType.Tool | Qt.WindowType.FramelessWindowHint)
        self.key, self.store = key, store
        self.setWindowTitle(title)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setMinimumSize(240, 180)
        self.drag_offset = None
        self.ready = False
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        self.body = QFrame()
        self.body.setObjectName('widgetBody')
        outer.addWidget(self.body)
        self.content = QVBoxLayout(self.body)
        self.content.setContentsMargins(16, 12, 16, 10)
        self.content.setSpacing(10)
        header = QHBoxLayout()
        self.title = QLabel(title)
        self.title.setObjectName('section')
        self.title.setCursor(Qt.CursorShape.SizeAllCursor)
        self.title.installEventFilter(self)
        header.addWidget(self.title, 1)
        hide = ActionButton('隐藏')
        hide.clicked.connect(self.hide)
        header.addWidget(hide)
        self.content.addLayout(header)
        self.footer = QHBoxLayout()
        self.message = QLabel('右键设置 · 拖动标题移动')
        self.message.setObjectName('muted')
        self.message.setWordWrap(True)
        self.footer.addWidget(self.message, 1)
        self.footer.addWidget(QSizeGrip(self), 0, Qt.AlignmentFlag.AlignRight)
        self.save_timer = QTimer(self)
        self.save_timer.setSingleShot(True)
        self.save_timer.setInterval(400)
        self.save_timer.timeout.connect(self.persist)
        self.fade = QPropertyAnimation(self, b'windowOpacity', self)
        self.fade.setDuration(200)
        self.fade.setEasingCurve(QEasingCurve.Type.InOutCubic)

    def finish(self, default_size=(320, 360)):
        self.content.addLayout(self.footer)
        state = self.store.data['widgets'].get(self.key, {})
        if not isinstance(state, dict):
            state = {}
        geometry = state.get('geometry', [])
        self.resize(*default_size)
        if isinstance(geometry, list) and len(geometry) == 4 and all(isinstance(v, int) for v in geometry):
            self.setGeometry(*geometry)
        screen = QApplication.screenAt(self.frameGeometry().center()) or QApplication.primaryScreen()
        area = screen.availableGeometry()
        self.resize(min(self.width(), area.width()), min(self.height(), area.height()))
        self.move(max(area.left(), min(self.x(), area.right() - self.width() + 1)), max(area.top(), min(self.y(), area.bottom() - self.height() + 1)))
        opacity = state.get('opacity', .95)
        self.opacity = float(opacity) if isinstance(opacity, (int, float)) and .65 <= opacity <= 1 else .95
        self.setWindowOpacity(self.opacity)
        self.ready = True

    def eventFilter(self, obj, event):
        if obj == self.title:
            if event.type() == QEvent.Type.MouseButtonPress and event.button() == Qt.MouseButton.LeftButton:
                self.drag_offset = event.globalPosition().toPoint() - self.pos()
                return True
            if event.type() == QEvent.Type.MouseMove and self.drag_offset is not None:
                self.move(event.globalPosition().toPoint() - self.drag_offset)
                return True
            if event.type() == QEvent.Type.MouseButtonRelease:
                self.drag_offset = None
                self.persist()
                return True
        return super().eventFilter(obj, event)

    def showEvent(self, event):
        super().showEvent(event)
        self.fade.stop()
        self.fade.setStartValue(max(.65, self.opacity - .15))
        self.fade.setEndValue(self.opacity)
        self.fade.start()
        if self.ready:
            self.save_timer.start()

    def hideEvent(self, event):
        super().hideEvent(event)
        if self.ready:
            self.persist()

    def moveEvent(self, event):
        super().moveEvent(event)
        if self.ready:
            self.save_timer.start()

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self.ready:
            self.save_timer.start()

    def persist(self):
        if not self.ready:
            return
        rect = self.geometry()
        self.store.data['widgets'][self.key] = {'geometry': [rect.x(), rect.y(), rect.width(), rect.height()],
                                              'opacity': self.opacity, 'visible': self.isVisible()}
        try:
            self.store.save()
        except OSError as exc:
            self.message.setText('保存失败：' + str(exc))

    def closeEvent(self, event):
        event.ignore()
        self.hide()

    def contextMenuEvent(self, event):
        menu = QMenu(self)
        menu.addAction('隐藏组件', self.hide)
        menu.addAction('回到屏幕中央', self.center)
        opacity = menu.addMenu('透明度')
        for value in (65, 80, 95, 100):
            opacity.addAction(str(value) + '%', lambda checked=False, v=value: self.set_opacity(v))
        if self.key == 'launcher':
            menu.addAction('停靠到右侧', lambda: self.dock('right'))
            menu.addAction('停靠到底部', lambda: self.dock('bottom'))
        menu.exec(event.globalPos())

    def set_opacity(self, value):
        self.opacity = value / 100
        self.setWindowOpacity(self.opacity)
        self.persist()

    def center(self):
        area = QApplication.primaryScreen().availableGeometry()
        self.move(area.center() - self.rect().center())
        self.persist()

    def dock(self, where):
        area = (self.screen() or QApplication.primaryScreen()).availableGeometry()
        if where == 'right':
            self.resize(300, min(480, area.height() - 32))
            self.move(area.right() - self.width() - 15, area.top() + 16)
        else:
            self.resize(min(660, area.width() - 32), 220)
            self.move(area.center().x() - self.width() // 2, area.bottom() - self.height() - 15)
        self.persist()


class TodoWidget(FloatingWidget):
    def __init__(self, store):
        super().__init__('todo', '待办清单', store)
        self.input = QLineEdit()
        self.input.setPlaceholderText('下一件要做的事')
        self.content.addWidget(self.input)
        self.note = QLineEdit()
        self.note.setPlaceholderText('备注（可选）')
        self.content.addWidget(self.note)
        row = QHBoxLayout()
        self.priority = QComboBox()
        self.priority.addItems(['普通', '重要', '紧急'])
        row.addWidget(self.priority)
        add = ActionButton('添加')
        add.clicked.connect(self.add)
        self.input.returnPressed.connect(self.add)
        row.addWidget(add)
        self.content.addLayout(row)
        self.list = QListWidget()
        self.content.addWidget(self.list, 1)
        delete = ActionButton('删除选中任务')
        delete.clicked.connect(self.delete)
        self.content.addWidget(delete)
        self.list.itemChanged.connect(self.changed)
        self.render()
        self.finish((340, 430))

    def render(self):
        self.list.blockSignals(True)
        self.list.clear()
        for task in self.store.data['tasks']:
            text = '[' + str(task.get('priority', '普通')) + '] ' + task['text']
            if task.get('note'):
                text += ' — ' + str(task['note'])
            item = QListWidgetItem(text)
            item.setFlags(item.flags() | Qt.ItemFlag.ItemIsUserCheckable)
            item.setCheckState(Qt.CheckState.Checked if task.get('done') else Qt.CheckState.Unchecked)
            self.list.addItem(item)
        self.list.blockSignals(False)

    def add(self):
        text = self.input.text().strip()
        if not text:
            return
        self.store.data['tasks'].append({'text': text, 'note': self.note.text().strip(), 'priority': self.priority.currentText(), 'done': False})
        self.input.clear()
        self.note.clear()
        self.render()
        self.persist()

    def changed(self, item):
        index = self.list.row(item)
        self.store.data['tasks'][index]['done'] = item.checkState() == Qt.CheckState.Checked
        self.persist()

    def delete(self):
        index = self.list.currentRow()
        if index >= 0:
            self.store.data['tasks'].pop(index)
            self.render()
            self.persist()


class NoteWidget(FloatingWidget):
    def __init__(self, store):
        super().__init__('note', '便签', store)
        self.editor = QTextEdit()
        self.editor.setAcceptRichText(False)
        self.editor.setPlaceholderText('留下一点想法…')
        self.editor.setPlainText(store.data['note'])
        self.content.addWidget(self.editor, 1)
        self.editor.textChanged.connect(self.changed)
        self.finish((340, 300))

    def changed(self):
        self.store.data['note'] = self.editor.toPlainText()
        self.save_timer.start()


class LauncherWidget(FloatingWidget):
    def __init__(self, store):
        super().__init__('launcher', '快速启动', store)
        self.list = QListWidget()
        self.list.itemClicked.connect(self.launch)
        self.content.addWidget(self.list, 1)
        row = QHBoxLayout()
        add = ActionButton('固定入口')
        add.clicked.connect(self.choose)
        row.addWidget(add)
        remove = ActionButton('移除')
        remove.clicked.connect(self.remove)
        row.addWidget(remove)
        self.content.addLayout(row)
        self.render()
        self.finish((320, 340))

    def render(self):
        self.list.clear()
        for target in self.store.data['launchers']:
            item = QListWidgetItem(target if target.startswith(('https://', 'http://')) else Path(target).name)
            item.setToolTip(target)
            item.setData(Qt.ItemDataRole.UserRole, target)
            self.list.addItem(item)

    def choose(self):
        menu = QMenu(self)
        menu.addAction('软件 / 文件', self.choose_file)
        menu.addAction('文件夹', self.choose_folder)
        menu.addAction('网页', self.choose_web)
        menu.exec(self.mapToGlobal(self.rect().center()))

    def pin(self, target):
        if target and target not in self.store.data['launchers']:
            self.store.data['launchers'].append(target)
            self.render()
            self.persist()

    def choose_file(self):
        target, _ = QFileDialog.getOpenFileName(self, '固定软件或文件')
        self.pin(target)

    def choose_folder(self):
        self.pin(QFileDialog.getExistingDirectory(self, '固定文件夹'))

    def choose_web(self):
        target, ok = QInputDialog.getText(self, '固定网页', '输入 http:// 或 https:// 地址')
        if ok:
            url = QUrl(target.strip())
            if url.scheme() not in ('http', 'https') or not url.host():
                self.message.setText('仅支持有效的 http / https 网页')
                return
            self.pin(target.strip())

    def launch(self, item):
        target = item.data(Qt.ItemDataRole.UserRole)
        try:
            if target.startswith(('https://', 'http://')):
                if not QDesktopServices.openUrl(QUrl(target)):
                    raise OSError('无法打开默认浏览器')
            elif Path(target).exists():
                os.startfile(target)
            else:
                raise OSError('路径已失效')
            self.message.setText('已启动：' + item.text())
        except OSError as exc:
            self.message.setText('启动失败：' + str(exc))

    def remove(self):
        index = self.list.currentRow()
        if index >= 0:
            self.store.data['launchers'].pop(index)
            self.render()
            self.persist()
