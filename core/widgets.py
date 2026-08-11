import os

from PySide6.QtWidgets import QLabel, QVBoxLayout, QWidget
from PySide6.QtCore import Qt
from PySide6.QtGui import QPainter, QPixmap, QBrush, QPainterPath, QColor

from core.config import config
from core.signal_bus import signal_bus


class PageHeader(QWidget):
    """Consistent title block for the four core work pages."""

    def __init__(self, title, subtitle, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(3)

        title_label = QLabel(title)
        title_label.setObjectName("pageTitle")
        subtitle_label = QLabel(subtitle)
        subtitle_label.setObjectName("pageSubtitle")
        layout.addWidget(title_label)
        layout.addWidget(subtitle_label)


def load_background_image(theme="light"):
    """加载背景图片的辅助函数"""
    try:
        if not config.use_background:
            return None
        
        custom_path = config.custom_background_light if theme == "light" else config.custom_background_dark
        if custom_path and os.path.exists(custom_path):
            return QPixmap(custom_path)
        
        if theme == "dark":
            image_name = "background-dark.png"
        else:
            image_name = "background-light.png"
        
        from core.config import get_resource_path
        image_path = get_resource_path(f"resources/img/{image_name}")
        
        if image_path.exists():
            return QPixmap(str(image_path))
        else:
            signal_bus.log_message.emit("WARNING", f"背景图片不存在: {image_path}", {})
            return None
    except Exception as e:
        signal_bus.log_message.emit("ERROR", f"加载背景图片失败: {str(e)}", {})
        return None


class BackgroundWidget(QWidget):
    """带背景图片的Widget"""
    
    def __init__(self, pixmap=None, theme="light", parent=None):
        super().__init__(parent)
        self.background_pixmap = pixmap
        self.theme = theme
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
    
    def set_background(self, pixmap, theme):
        """设置背景图片和主题"""
        self.background_pixmap = pixmap
        self.theme = theme
        self.update()
    
    def paintEvent(self, event):
        """绘制背景图片"""
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        
        # 获取widget尺寸
        widget_rect = self.rect()
        
        # 创建圆角裁剪路径
        path = QPainterPath()
        path.addRoundedRect(widget_rect.x(), widget_rect.y(), widget_rect.width(), widget_rect.height(), 8, 8)
        painter.setClipPath(path)
        
        if self.background_pixmap:
            # 缩放背景图片以适应widget大小
            scaled_pixmap = self.background_pixmap.scaled(
                widget_rect.size(),
                Qt.AspectRatioMode.KeepAspectRatioByExpanding,
                Qt.TransformationMode.SmoothTransformation
            )
            
            # 计算居中位置
            x = (widget_rect.width() - scaled_pixmap.width()) // 2
            y = (widget_rect.height() - scaled_pixmap.height()) // 2
            
            # 绘制背景图片
            painter.drawPixmap(x, y, scaled_pixmap)
            
            # 绘制半透明遮罩以保证文字可读性
            overlay_color = QColor(0, 0, 0, 120) if self.theme == "dark" else QColor(255, 255, 255, 120)
            painter.fillRect(widget_rect, QBrush(overlay_color))
        else:
            # 没有背景图片时使用纯色背景
            bg_color = QColor(30, 30, 30) if self.theme == "dark" else QColor(248, 249, 250)
            painter.fillRect(widget_rect, QBrush(bg_color))


