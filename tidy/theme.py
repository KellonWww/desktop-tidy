from __future__ import annotations
from PySide6.QtCore import QObject, QTimer, QVariantAnimation, QEasingCurve
from PySide6.QtGui import QColor, QPalette
from PySide6.QtWidgets import QPushButton

PALETTES = {
    'light': {'bg': '#f3f4f6', 'surface': '#ffffff', 'text': '#24272c', 'muted': '#636b76', 'line': '#d8dde5', 'accent': '#2463c5', 'hover': '#e7ebf1', 'field': '#f8f9fb'},
    'dark': {'bg': '#202226', 'surface': '#2b2e34', 'text': '#f0f2f5', 'muted': '#b0b7c3', 'line': '#494e58', 'accent': '#79aaff', 'hover': '#3a3f48', 'field': '#24272d'},
}

class ActionButton(QPushButton):
    def __init__(self, text, primary=False, parent=None):
        super().__init__(text, parent)
        self.primary = primary
        self.tokens = PALETTES['light']
        self.mix = 0.0
        self.animation = QVariantAnimation(self)
        self.animation.setDuration(180)
        self.animation.setEasingCurve(QEasingCurve.Type.InOutCubic)
        self.animation.valueChanged.connect(self._frame)
        self.setMinimumHeight(36)
        self.refresh()

    def _frame(self, value):
        self.mix = float(value)
        self.refresh()

    def refresh(self):
        p = self.tokens
        a = QColor(p['accent'] if self.primary else p['surface'])
        b = QColor(a.lighter(110) if self.primary else p['hover'])
        color = QColor(*[round(x + (y - x) * self.mix) for x, y in zip(a.getRgb()[:3], b.getRgb()[:3])])
        foreground = '#ffffff' if self.primary and p is PALETTES['light'] else ('#17243b' if self.primary else p['text'])
        self.setStyleSheet('QPushButton {background:' + color.name() + ';color:' + foreground + ';border:1px solid ' + (p['accent'] if self.primary else p['line']) + ';border-radius:9px;padding:8px 16px;} QPushButton:pressed {background:' + b.darker(115).name() + ';} QPushButton:disabled {background:' + p['field'] + ';color:' + p['muted'] + ';border-color:' + p['line'] + ';} QPushButton:focus {border:2px solid ' + p['accent'] + ';}')

    def _animate(self, end):
        self.animation.stop()
        self.animation.setStartValue(self.mix)
        self.animation.setEndValue(end)
        self.animation.start()

    def enterEvent(self, event):
        self._animate(1.0)
        super().enterEvent(event)

    def leaveEvent(self, event):
        self._animate(0.0)
        super().leaveEvent(event)

class Theme(QObject):
    def __init__(self, app):
        super().__init__(app)
        self.app = app
        self.current = None
        self.timer = QTimer(self)
        self.timer.timeout.connect(self.update)
        self.timer.start(2500)
        self.update()

    def detect(self):
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize') as key:
                return 'light' if winreg.QueryValueEx(key, 'AppsUseLightTheme')[0] else 'dark'
        except (ImportError, OSError):
            return 'light'

    def update(self, force=False):
        name = self.detect()
        if name == self.current and not force:
            return
        try:
            self.apply(name)
        except Exception:
            self.apply('light')

    def apply(self, name):
        p = PALETTES[name]
        palette = QPalette()
        for role, token in [(QPalette.ColorRole.Window, 'bg'), (QPalette.ColorRole.WindowText, 'text'),
                            (QPalette.ColorRole.Base, 'field'), (QPalette.ColorRole.Text, 'text'),
                            (QPalette.ColorRole.Button, 'surface'), (QPalette.ColorRole.ButtonText, 'text'),
                            (QPalette.ColorRole.Highlight, 'accent')]:
            palette.setColor(role, QColor(p[token]))
        palette.setColor(QPalette.ColorRole.HighlightedText, QColor('#ffffff' if name == 'light' else '#17243b'))
        self.app.setPalette(palette)
        css = [
            'QWidget {{color:{text};font-family:Segoe UI;font-size:13px;}}',
            'QMainWindow,QDialog {{background:{bg};}}',
            'QFrame#panel {{background:{surface};border:1px solid {line};border-radius:16px;}}',
            'QLabel#title {{font-size:26px;font-weight:600;}}',
            'QLabel#section {{font-size:16px;font-weight:600;}}',
            'QLabel#muted {{color:{muted};}}',
            'QLabel#feedback {{background:{field};border-radius:10px;padding:12px;color:{text};}}',
            'QLineEdit,QTextEdit,QPlainTextEdit,QComboBox,QSpinBox,QTableWidget,QTreeWidget,QListWidget {{background:{field};border:1px solid {line};border-radius:8px;padding:6px;selection-background-color:{accent};}}',
            'QComboBox QAbstractItemView {{background:{surface};selection-background-color:{accent};}}',
            'QHeaderView::section {{background:{surface};color:{muted};border:0;padding:9px;}}',
            'QPushButton {{background:{surface};border:1px solid {line};border-radius:9px;padding:8px 14px;}}',
            'QPushButton:hover {{background:{hover};}}',
            'QPushButton:pressed {{background:{field};}}',
            'QPushButton:disabled {{color:{muted};}}',
            'QCheckBox {{spacing:9px;}}',
            'QCheckBox::indicator {{width:17px;height:17px;border-radius:5px;border:1px solid {line};background:{field};}}',
            'QCheckBox::indicator:checked {{background:{accent};border-color:{accent};}}',
            'QMenu {{background:{surface};border:1px solid {line};padding:6px;}}',
            'QMenu::item {{padding:7px 18px;}}',
            'QMenu::item:selected {{background:{hover};border-radius:6px;}}',
            'QFrame#widgetBody {{background:{surface};border:1px solid {line};border-radius:16px;}}',
            'QToolTip {{background:{surface};color:{text};border:1px solid {line};padding:6px;}}',
        ]
        self.app.setStyleSheet(' '.join(rule.format(**p) for rule in css))
        for widget in self.app.allWidgets():
            if isinstance(widget, ActionButton):
                widget.tokens = p
                widget.refresh()
        self.current = name
