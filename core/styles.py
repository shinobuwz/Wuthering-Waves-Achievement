"""Application-wide visual tokens and Qt styles."""

from PySide6.QtGui import QIcon

from core.config import get_resource_path


ICON_PATH = get_resource_path("resources/icons")


class ColorPalette:
    """Shared semantic colors for light and dark themes."""

    class Opacity:
        # Kept for compatibility with older dialogs.
        GROUPBOX = 255
        TEXT_INPUT = 255
        TAB_WIDGET_PANE = 255
        TAB_WIDGET_TAB = 255
        TAB_SELECTED = 255
        TAB_HOVER = 255
        SETTINGS_DESC = 255
        TABLE_HEADER = 255
        TABLE_SELECTION = 255
        COMBOBOX = 255
        COMBOBOX_VIEW = 255
        MAIN_WINDOW = 255
        DIALOG = 255
        STATUSBAR_LIGHT = 255
        STATUSBAR_DARK = 255
        MESSAGEBOX = 255
        SCROLLBAR = 255
        HELP_TEXT = 255

    class Light:
        APP_BG = "#eef2f0"
        SIDEBAR_BG = "#e5ebe8"
        SURFACE = "#ffffff"
        SURFACE_ALT = "#f5f8f6"
        SURFACE_HOVER = "#eaf1ee"
        SUCCESS = "#087f5b"
        SUCCESS_HOVER = "#066b4c"
        SUCCESS_PRESSED = "#05583f"
        ACCENT = "#087f5b"
        ACCENT_SOFT = "#dcefe8"
        REWARD = "#9a6b12"
        BG_GRAY = SURFACE_ALT
        TEXT_PRIMARY = "#17211e"
        TEXT_SECONDARY = "#4f5f59"
        TEXT_GRAY = "#6b7a75"
        BORDER = "#d3ddd9"
        BORDER_STRONG = "#b9c8c2"
        DISABLED_BG = "#dce3e0"
        DISABLED_TEXT = "#87958f"
        TABLE_GRID = "#e2e9e6"
        TABLE_SELECTION = "#dcefe8"
        TABLE_HEADER = "#f0f5f2"
        SCROLLBAR_BG = "#e9efec"
        SCROLLBAR_HANDLE = "#aebdb7"
        TAB_HOVER = SURFACE_HOVER
        SETTINGS_DESC_BG = SURFACE_ALT
        SETTINGS_DESC_TEXT = TEXT_SECONDARY
        DANGER = "#b42318"
        WARNING = "#9a6700"

    class Dark:
        APP_BG = "#101412"
        SIDEBAR_BG = "#141a17"
        SURFACE = "#181f1c"
        SURFACE_ALT = "#1e2723"
        SURFACE_HOVER = "#25312c"
        SUCCESS = "#48d6a2"
        SUCCESS_HOVER = "#61e2b3"
        SUCCESS_PRESSED = "#34ba89"
        ACCENT = "#48d6a2"
        ACCENT_SOFT = "#173c30"
        REWARD = "#d8b35a"
        BG_GRAY = SURFACE_ALT
        TEXT_PRIMARY = "#edf4f1"
        TEXT_SECONDARY = "#b5c2bd"
        TEXT_GRAY = "#93a39d"
        BORDER = "#303b36"
        BORDER_STRONG = "#43524c"
        DISABLED_BG = "#29312e"
        DISABLED_TEXT = "#71807a"
        TABLE_GRID = "#28322e"
        TABLE_SELECTION = "#1d4a3b"
        TABLE_HEADER = "#202925"
        SCROLLBAR_BG = "#1a211e"
        SCROLLBAR_HANDLE = "#53635d"
        TAB_HOVER = SURFACE_HOVER
        SETTINGS_DESC_BG = SURFACE_ALT
        SETTINGS_DESC_TEXT = TEXT_SECONDARY
        DANGER = "#ff776d"
        WARNING = "#e3b341"


def _colors(theme):
    return ColorPalette.Dark if theme == "dark" else ColorPalette.Light


def _get_rgba_color(r, g, b, opacity):
    """Compatibility helper retained for external callers."""
    return f"rgba({r}, {g}, {b}, {opacity})"


class _BaseStylesClass:
    @staticmethod
    def get_button_style(theme="light"):
        c = _colors(theme)
        primary_text = "#082019" if theme == "dark" else "#ffffff"
        return f"""
        QPushButton {{
            min-height: 30px;
            padding: 0 14px;
            border: 1px solid {c.BORDER_STRONG};
            border-radius: 5px;
            background: {c.SURFACE_ALT};
            color: {c.TEXT_PRIMARY};
            font-size: 12px;
            font-weight: 600;
        }}
        QPushButton:hover {{ background: {c.SURFACE_HOVER}; border-color: {c.ACCENT}; }}
        QPushButton:pressed {{ background: {c.ACCENT_SOFT}; }}
        QPushButton:disabled {{
            background: {c.DISABLED_BG}; color: {c.DISABLED_TEXT}; border-color: {c.BORDER};
        }}
        QPushButton[buttonRole="primary"] {{
            background: {c.SUCCESS}; color: {primary_text}; border-color: {c.SUCCESS};
        }}
        QPushButton[buttonRole="primary"]:hover {{ background: {c.SUCCESS_HOVER}; border-color: {c.SUCCESS_HOVER}; }}
        QPushButton[buttonRole="primary"]:pressed {{ background: {c.SUCCESS_PRESSED}; border-color: {c.SUCCESS_PRESSED}; }}
        QPushButton[buttonRole="quiet"] {{ background: transparent; border-color: transparent; color: {c.TEXT_SECONDARY}; }}
        QPushButton[buttonRole="quiet"]:hover {{ background: {c.SURFACE_HOVER}; color: {c.TEXT_PRIMARY}; }}
        QPushButton[buttonRole="danger"] {{ background: transparent; color: {c.DANGER}; border-color: {c.BORDER}; }}
        """

    @staticmethod
    def get_groupbox_style(theme="light"):
        c = _colors(theme)
        return f"""
        QGroupBox {{
            color: {c.TEXT_PRIMARY};
            background: {c.SURFACE};
            border: 1px solid {c.BORDER};
            border-radius: 6px;
            margin-top: 10px;
            padding-top: 12px;
            font-size: 12px;
            font-weight: 600;
        }}
        QGroupBox::title {{
            subcontrol-origin: margin;
            left: 10px;
            padding: 0 6px;
            color: {c.TEXT_SECONDARY};
        }}
        """

    @staticmethod
    def get_tab_widget_style(theme="light"):
        c = _colors(theme)
        return f"""
        QTabWidget::pane {{ border: 1px solid {c.BORDER}; background: {c.SURFACE}; border-radius: 6px; }}
        QTabBar::tab {{
            min-height: 30px; min-width: 82px; padding: 0 12px;
            border: none; border-bottom: 2px solid transparent;
            background: transparent; color: {c.TEXT_SECONDARY}; font-weight: 600;
        }}
        QTabBar::tab:hover {{ color: {c.TEXT_PRIMARY}; background: {c.SURFACE_HOVER}; }}
        QTabBar::tab:selected {{ color: {c.ACCENT}; border-bottom-color: {c.ACCENT}; }}
        """

    @staticmethod
    def get_text_input_style(theme="light"):
        c = _colors(theme)
        return f"""
        QLineEdit, QTextEdit, QPlainTextEdit, QTableWidget, QListWidget {{
            background: {c.SURFACE}; color: {c.TEXT_PRIMARY};
            border: 1px solid {c.BORDER}; border-radius: 5px;
            selection-background-color: {c.TABLE_SELECTION};
            selection-color: {c.TEXT_PRIMARY};
        }}
        QLineEdit {{ min-height: 30px; padding: 0 9px; }}
        QLineEdit:focus, QTextEdit:focus, QPlainTextEdit:focus {{ border-color: {c.ACCENT}; }}
        QTableWidget {{ gridline-color: {c.TABLE_GRID}; alternate-background-color: {c.SURFACE_ALT}; outline: 0; }}
        QTableWidget::item {{ padding: 6px; border: none; }}
        QTableWidget::item:selected {{ background: {c.TABLE_SELECTION}; color: {c.TEXT_PRIMARY}; }}
        QHeaderView::section {{
            min-height: 32px; padding: 0 8px;
            background: {c.TABLE_HEADER}; color: {c.TEXT_SECONDARY};
            border: none; border-right: 1px solid {c.TABLE_GRID}; border-bottom: 1px solid {c.BORDER};
            font-size: 11px; font-weight: 600;
        }}
        QTableCornerButton::section {{ background: {c.TABLE_HEADER}; border: none; border-bottom: 1px solid {c.BORDER}; }}
        QListWidget::item {{ min-height: 30px; padding: 0 8px; }}
        QListWidget::item:selected {{ background: {c.TABLE_SELECTION}; color: {c.TEXT_PRIMARY}; }}
        """

    @staticmethod
    def get_label_style(theme="light", label_type="normal"):
        c = _colors(theme)
        color = c.TEXT_GRAY if label_type == "gray" else c.TEXT_PRIMARY
        return f"QLabel {{ color: {color}; }}"

    @staticmethod
    def get_combobox_style(theme="light"):
        c = _colors(theme)
        return f"""
        QComboBox {{
            min-height: 30px; padding: 0 28px 0 9px;
            background: {c.SURFACE}; color: {c.TEXT_PRIMARY};
            border: 1px solid {c.BORDER}; border-radius: 5px;
        }}
        QComboBox:hover, QComboBox:focus {{ border-color: {c.ACCENT}; }}
        QComboBox::drop-down {{ width: 24px; border: none; }}
        QComboBox QAbstractItemView {{
            background: {c.SURFACE}; color: {c.TEXT_PRIMARY};
            border: 1px solid {c.BORDER_STRONG};
            selection-background-color: {c.TABLE_SELECTION};
            selection-color: {c.TEXT_PRIMARY}; outline: 0;
        }}
        """


BaseStyles = _BaseStylesClass()


def get_icon(icon_name):
    icon_path = ICON_PATH / f"{icon_name}.ico"
    return QIcon(str(icon_path)) if icon_path.exists() else QIcon()


def get_main_window_style(theme="light"):
    c = _colors(theme)
    sidebar_overlay = "rgba(20, 26, 23, 218)" if theme == "dark" else "rgba(229, 235, 232, 218)"
    content_overlay = "rgba(16, 20, 18, 150)" if theme == "dark" else "rgba(238, 242, 240, 150)"
    return f"""
    QMainWindow {{ background: transparent; font-family: 'Microsoft YaHei', 'Segoe UI', sans-serif; }}
    QWidget#appRoot {{ background: transparent; border: 1px solid {c.BORDER_STRONG}; border-radius: 8px; }}
    QFrame#appSidebar {{ background: {sidebar_overlay}; border: none; border-right: 1px solid {c.BORDER}; }}
    QFrame#contentArea {{ background: {content_overlay}; border: none; }}
    QLabel#brandTitle {{ color: {c.TEXT_PRIMARY}; font-size: 15px; font-weight: 700; }}
    QLabel#brandSubtitle {{ color: {c.TEXT_GRAY}; font-size: 10px; }}
    QLabel#sidebarSection {{ color: {c.TEXT_GRAY}; font-size: 10px; font-weight: 600; }}
    QPushButton#navButton {{
        min-height: 42px; padding: 0 14px; text-align: left;
        background: transparent; color: {c.TEXT_SECONDARY};
        border: none; border-left: 3px solid transparent; border-radius: 0;
        font-size: 13px; font-weight: 600;
    }}
    QPushButton#navButton:hover {{ background: {c.SURFACE_HOVER}; color: {c.TEXT_PRIMARY}; }}
    QPushButton#navButton:checked {{
        background: {c.ACCENT_SOFT}; color: {c.ACCENT}; border-left-color: {c.ACCENT};
    }}
    QLabel#pageTitle {{ color: {c.TEXT_PRIMARY}; font-size: 22px; font-weight: 700; }}
    QLabel#pageSubtitle {{ color: {c.TEXT_GRAY}; font-size: 11px; }}
    QLabel#sectionLabel {{ color: {c.TEXT_SECONDARY}; font-size: 11px; font-weight: 600; }}
    QFrame#toolbar, QFrame#metricStrip, QFrame#statusStrip {{
        background: {c.SURFACE}; border: 1px solid {c.BORDER}; border-radius: 6px;
    }}
    QFrame#metricItem {{ background: transparent; border: none; border-right: 1px solid {c.BORDER}; }}
    QLabel#metricValue {{ color: {c.TEXT_PRIMARY}; font-size: 18px; font-weight: 700; }}
    QLabel#metricLabel {{ color: {c.TEXT_GRAY}; font-size: 10px; }}
    QLabel#statusGood {{ color: {c.ACCENT}; font-weight: 600; }}
    QLabel#statusWarning {{ color: {c.WARNING}; font-weight: 600; }}
    QLabel {{ color: {c.TEXT_PRIMARY}; }}
    QCheckBox {{ color: {c.TEXT_SECONDARY}; spacing: 7px; }}
    QCheckBox::indicator {{ width: 15px; height: 15px; border: 1px solid {c.BORDER_STRONG}; border-radius: 3px; background: {c.SURFACE}; }}
    QCheckBox::indicator:checked {{ background: {c.ACCENT}; border-color: {c.ACCENT}; }}
    QProgressBar {{ min-height: 7px; max-height: 7px; border: none; border-radius: 3px; background: {c.SURFACE_ALT}; text-align: center; color: transparent; }}
    QProgressBar::chunk {{ border-radius: 3px; background: {c.ACCENT}; }}
    QToolTip {{ color: {c.TEXT_PRIMARY}; background: {c.SURFACE_ALT}; border: 1px solid {c.BORDER_STRONG}; padding: 5px; }}
    QStatusBar {{ background: {c.SURFACE}; color: {c.TEXT_SECONDARY}; border-top: 1px solid {c.BORDER}; }}
    QMessageBox, QDialog {{ background: {c.APP_BG}; color: {c.TEXT_PRIMARY}; }}
    """ + BaseStyles.get_button_style(theme) + BaseStyles.get_groupbox_style(theme) + BaseStyles.get_tab_widget_style(theme) + BaseStyles.get_text_input_style(theme) + BaseStyles.get_combobox_style(theme) + get_scrollbar_style(theme)


def get_dialog_style(theme="light"):
    c = _colors(theme)
    return f"""
    QDialog {{ background: transparent; font-family: 'Microsoft YaHei', 'Segoe UI', sans-serif; }}
    QDialog #dialogContainer {{ background: {c.APP_BG}; border: 1px solid {c.BORDER_STRONG}; border-radius: 8px; }}
    QLabel {{ color: {c.TEXT_PRIMARY}; }}
    QCheckBox {{ color: {c.TEXT_SECONDARY}; }}
    """ + BaseStyles.get_button_style(theme) + BaseStyles.get_groupbox_style(theme) + BaseStyles.get_tab_widget_style(theme) + BaseStyles.get_text_input_style(theme) + BaseStyles.get_combobox_style(theme) + get_scrollbar_style(theme)


def get_settings_desc_style(theme="light"):
    c = _colors(theme)
    return f"color: {c.SETTINGS_DESC_TEXT}; font-size: 12px; background: {c.SETTINGS_DESC_BG}; padding: 10px; border-radius: 5px;"


def get_button_style(theme="light"):
    return BaseStyles.get_button_style(theme)


def get_font_gray_style(theme="light"):
    return BaseStyles.get_label_style(theme, "gray")


def get_scrollbar_style(theme="light"):
    c = _colors(theme)
    return f"""
    QScrollBar:vertical {{ background: {c.SCROLLBAR_BG}; width: 10px; margin: 0; }}
    QScrollBar::handle:vertical {{ background: {c.SCROLLBAR_HANDLE}; border-radius: 5px; min-height: 28px; margin: 2px; }}
    QScrollBar:horizontal {{ background: {c.SCROLLBAR_BG}; height: 10px; margin: 0; }}
    QScrollBar::handle:horizontal {{ background: {c.SCROLLBAR_HANDLE}; border-radius: 5px; min-width: 28px; margin: 2px; }}
    QScrollBar::add-line, QScrollBar::sub-line {{ width: 0; height: 0; }}
    QScrollBar::add-page, QScrollBar::sub-page {{ background: transparent; }}
    """


def get_scroll_area_style(theme="light"):
    return "QScrollArea { background: transparent; border: none; } QScrollArea > QWidget > QWidget { background: transparent; }" + get_scrollbar_style(theme)


def get_label_style(theme="light"):
    return BaseStyles.get_label_style(theme)


def get_notification_style(theme="light"):
    c = _colors(theme)
    return f"QLabel {{ background: {c.SURFACE_ALT}; color: {c.TEXT_PRIMARY}; border: 1px solid {c.ACCENT}; padding: 10px 16px; border-radius: 5px; font-weight: 600; }}"


def get_text_input_style(theme="light"):
    return BaseStyles.get_text_input_style(theme)


def get_help_text_style(theme="light"):
    c = _colors(theme)
    return f"QLabel {{ background: {c.SURFACE}; color: {c.TEXT_PRIMARY}; border: 1px solid {c.BORDER}; padding: 18px; border-radius: 6px; }} QLabel a {{ color: {c.ACCENT}; }}"
