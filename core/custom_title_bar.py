"""Compact frameless window title bar shared by app windows and dialogs."""

from PySide6.QtCore import QPoint, Qt, Signal
from PySide6.QtGui import QMouseEvent
from PySide6.QtWidgets import QApplication, QHBoxLayout, QLabel, QPushButton, QWidget

from core.config import config
from core.signal_bus import signal_bus
from core.styles import ColorPalette


class ThemeToggleButton(QPushButton):
    """Small theme toggle with the legacy statusChanged signal."""

    statusChanged = Signal(bool)

    def __init__(self, parent=None, size=30):
        super().__init__(parent)
        self.setObjectName("themeToggle")
        self.setFixedSize(38, 30)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setToolTip("切换明暗主题")
        self.clicked.connect(self.toggle)
        self.sync_state()

    def sync_state(self):
        is_dark = config.theme == "dark"
        self.setText("☀" if is_dark else "◐")
        self.setProperty("darkMode", is_dark)

    def toggle(self):
        config.theme = "light" if config.theme == "dark" else "dark"
        config.save_config()
        self.sync_state()
        is_dark = config.theme == "dark"
        self.statusChanged.emit(is_dark)
        signal_bus.log_message.emit(
            "SUCCESS", f"已切换到{'深色' if is_dark else '浅色'}模式", {}
        )


# Compatibility name used by older imports.
SunMoonButton = ThemeToggleButton


class CustomTitleBarButton(QPushButton):
    """Compatibility wrapper for the previous title-bar button class."""

    def __init__(self, color=None, parent=None):
        super().__init__(parent)
        self._color = color or ""
        self.setFixedSize(40, 34)


class CustomTitleBar(QWidget):
    """Native-feeling title bar for frameless Qt windows."""

    def __init__(self, parent=None, show_theme_toggle=False):
        super().__init__(parent)
        self.parent_window = parent
        self.drag_position = QPoint()
        self.show_theme_toggle = show_theme_toggle
        self.setObjectName("customTitleBar")
        self.setFixedHeight(44)
        self.init_ui()
        self.update_theme()

    def init_ui(self):
        layout = QHBoxLayout(self)
        layout.setContentsMargins(12, 0, 6, 0)
        layout.setSpacing(8)

        app = QApplication.instance()
        self.icon_label = QLabel()
        self.icon_label.setObjectName("titleIcon")
        self.icon_label.setFixedSize(22, 22)
        if app and not app.windowIcon().isNull():
            self.icon_label.setPixmap(app.windowIcon().pixmap(20, 20))
        self.icon_label.setCursor(Qt.CursorShape.ArrowCursor)
        layout.addWidget(self.icon_label)

        app_name = app.applicationName() if app else "鸣潮成就管理器"
        app_version = app.applicationVersion() if app else ""
        title_text = f"{app_name}  {app_version}" if app_version else app_name
        self.title_label = QLabel(title_text)
        self.title_label.setObjectName("windowTitle")
        layout.addWidget(self.title_label)
        layout.addStretch()

        if self.show_theme_toggle:
            self.sun_moon_btn = ThemeToggleButton(self)
            self.sun_moon_btn.statusChanged.connect(self.on_theme_changed)
            layout.addWidget(self.sun_moon_btn)

        self.min_btn = self._window_button("−", "最小化", self.minimize_window)
        self.max_btn = self._window_button("□", "最大化或还原", self.maximize_restore_window)
        self.close_btn = self._window_button("×", "关闭", self.close_window, close=True)
        layout.addWidget(self.min_btn)
        layout.addWidget(self.max_btn)
        layout.addWidget(self.close_btn)

    def _window_button(self, text, tooltip, callback, close=False):
        button = CustomTitleBarButton(parent=self)
        button.setText(text)
        button.setToolTip(tooltip)
        button.setObjectName("closeWindowButton" if close else "windowButton")
        button.setCursor(Qt.CursorShape.PointingHandCursor)
        button.clicked.connect(callback)
        return button

    def on_theme_changed(self, is_night):
        saved_pos = self.parent_window.pos() if self.parent_window else None
        saved_size = self.parent_window.size() if self.parent_window else None
        self.update_theme()
        signal_bus.theme_changed.emit(config.theme)
        if self.parent_window and hasattr(self.parent_window, "apply_theme"):
            self.parent_window.apply_theme()
        if self.parent_window and saved_pos is not None:
            self.parent_window.move(saved_pos)
            self.parent_window.resize(saved_size)

    def update_theme(self):
        colors = ColorPalette.Dark if config.theme == "dark" else ColorPalette.Light
        if hasattr(self, "sun_moon_btn"):
            self.sun_moon_btn.sync_state()
        self.setStyleSheet(f"""
            QWidget#customTitleBar {{
                background: {colors.SURFACE};
                border: none;
                border-bottom: 1px solid {colors.BORDER};
                border-top-left-radius: 8px;
                border-top-right-radius: 8px;
            }}
            QLabel#windowTitle {{ color: {colors.TEXT_SECONDARY}; font-size: 11px; font-weight: 600; }}
            QPushButton#windowButton, QPushButton#closeWindowButton, QPushButton#themeToggle {{
                min-width: 38px; max-width: 38px; min-height: 30px; max-height: 30px;
                padding: 0; border: none; border-radius: 4px;
                background: transparent; color: {colors.TEXT_SECONDARY};
                font-size: 15px; font-weight: 500;
            }}
            QPushButton#windowButton:hover, QPushButton#themeToggle:hover {{
                background: {colors.SURFACE_HOVER}; color: {colors.TEXT_PRIMARY};
            }}
            QPushButton#closeWindowButton:hover {{ background: #c42b1c; color: white; }}
        """)

    def minimize_window(self):
        if self.parent_window:
            self.parent_window.showMinimized()

    def maximize_restore_window(self):
        if not self.parent_window:
            return
        if self.parent_window.isMaximized():
            self.parent_window.showNormal()
            self.max_btn.setText("□")
        else:
            self.parent_window.showMaximized()
            self.max_btn.setText("❐")
        self.update_theme()

    def close_window(self):
        if self.parent_window:
            self.parent_window.close()

    def mousePressEvent(self, event: QMouseEvent):
        if event.button() == Qt.MouseButton.LeftButton and self.parent_window:
            self.drag_position = (
                event.globalPosition().toPoint()
                - self.parent_window.frameGeometry().topLeft()
            )
            event.accept()

    def mouseMoveEvent(self, event: QMouseEvent):
        if event.buttons() == Qt.MouseButton.LeftButton and self.parent_window:
            if self.parent_window.isMaximized():
                self.parent_window.showNormal()
                self.drag_position = QPoint(self.parent_window.width() // 2, 20)
            self.parent_window.move(event.globalPosition().toPoint() - self.drag_position)
            event.accept()

    def mouseDoubleClickEvent(self, event: QMouseEvent):
        if event.button() == Qt.MouseButton.LeftButton:
            self.maximize_restore_window()
            event.accept()
