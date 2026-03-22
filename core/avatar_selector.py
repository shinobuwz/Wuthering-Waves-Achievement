import os
import logging

from PySide6.QtWidgets import (QWidget, QMainWindow, QVBoxLayout, QLabel,
                               QScrollArea, QGridLayout, QFrame)
from PySide6.QtCore import Qt, Signal, QTimer
from PySide6.QtGui import QPixmap, QPainter, QMouseEvent, QPainterPath

from core.config import config, get_resource_path
from core.signal_bus import signal_bus

logger = logging.getLogger(__name__)
from core.widgets import BackgroundWidget, load_background_image
from core.custom_title_bar import CustomTitleBar
from core.styles import get_dialog_style, get_scroll_area_style, get_label_style


class AvatarItem(QWidget):
    """单个头像项"""
    clicked = Signal(str, str)  # 信号：头像路径，头像名称
    
    def __init__(self, avatar_path, avatar_name):
        super().__init__()
        self.avatar_path = avatar_path
        self.avatar_name = avatar_name
        self.is_selected = False
        self.init_ui()
    
    def init_ui(self):
        """初始化UI"""
        layout = QVBoxLayout(self)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setSpacing(5)
        
        # 头像容器（圆形）
        self.avatar_container = QLabel()
        self.avatar_container.setFixedSize(70, 70)
        self.avatar_container.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.avatar_container.setCursor(Qt.CursorShape.PointingHandCursor)
        
        # 继承主窗口样式，不设置额外样式
        
        # 加载头像图片
        self.load_avatar()
        
        # 头像名称标签
        name_label = QLabel(self.avatar_name)
        name_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        name_label.setMaximumWidth(80)
        name_label.setWordWrap(True)
        name_label.setStyleSheet(get_label_style(config.theme))
        

        
        layout.addWidget(self.avatar_container)
        layout.addWidget(name_label)

    
    def load_avatar(self):
        """加载头像图片"""
        if os.path.exists(self.avatar_path):
            pixmap = QPixmap(self.avatar_path)
            if not pixmap.isNull():
                # 创建圆形头像
                circular_pixmap = self.create_circular_pixmap(pixmap, 60)
                self.avatar_container.setPixmap(circular_pixmap)

    def create_circular_pixmap(self, pixmap, diameter):
        """创建圆形图片"""
        circular_pixmap = QPixmap(diameter, diameter)
        circular_pixmap.fill(Qt.GlobalColor.transparent)

        painter = QPainter(circular_pixmap)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        # 创建圆形路径 - 使用 QPainterPath
        path = QPainterPath()
        path.addEllipse(0, 0, diameter, diameter)

        painter.setClipPath(path)

        # 缩放并居中图片
        scaled_pixmap = pixmap.scaled(
            diameter, diameter,
            Qt.AspectRatioMode.KeepAspectRatioByExpanding,
            Qt.TransformationMode.SmoothTransformation
        )

        # 计算居中位置
        x = (diameter - scaled_pixmap.width()) // 2
        y = (diameter - scaled_pixmap.height()) // 2

        painter.drawPixmap(x, y, scaled_pixmap)
        painter.end()

        return circular_pixmap
    
    def mousePressEvent(self, event: QMouseEvent):
        """鼠标点击事件"""
        if event.button() == Qt.MouseButton.LeftButton:
            # 发送点击信号
            self.clicked.emit(self.avatar_path, self.avatar_name)
            
            # 添加选中效果
            self.set_selected(True)
            
            # 延迟后恢复
            QTimer.singleShot(200, lambda: self.set_selected(False))
    
    def set_selected(self, selected):
        """设置选中状态"""
        self.is_selected = selected
        # 简化选中状态，只设置一个简单的边框变化
        if selected:
            self.avatar_container.setStyleSheet("border: 3px solid #4a90e2; border-radius: 35px;")
        else:
            self.avatar_container.setStyleSheet("border: 2px solid #dee2e6; border-radius: 35px;")


class AvatarSelector(QMainWindow):
    """头像选择器窗口"""
    avatar_selected = Signal(str, str)  # 信号：头像路径，头像名称
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.parent_window = parent
        self.init_ui()
        self.load_avatars()
        
        # 监听主题切换信号
        signal_bus.theme_changed.connect(self.on_theme_changed)
        
    def init_ui(self):
        """初始化UI"""
        # 设置无边框窗口，完全去掉系统标题栏
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.Dialog)
        # 设置窗口透明以显示圆角
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        
        # 调整窗口尺寸以完整显示6个头像
        self.setFixedSize(750, 450)
        
        # 设置样式
        self.setStyleSheet(get_dialog_style(config.theme))
        
        # 加载背景图片
        self.background_pixmap = load_background_image(config.theme)
        
        # 创建带背景图片的中央部件
        central_widget = BackgroundWidget(self.background_pixmap, config.theme)
        self.setCentralWidget(central_widget)
        
        # 主布局
        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)
        
        # 添加自定义标题栏
        self.title_bar = CustomTitleBar(self)
        main_layout.addWidget(self.title_bar)
        
        # 内容区域
        content_widget = QWidget()
        content_layout = QVBoxLayout(content_widget)
        content_layout.setContentsMargins(20, 20, 20, 20)
        content_layout.setSpacing(15)
        main_layout.addWidget(content_widget)
        
        
        
        # 滚动区域
        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        scroll_area.setFrameShape(QFrame.Shape.NoFrame)
        
        # 使用现有的滚动区域样式（已包含滚动条）
        scroll_area.setStyleSheet(get_scroll_area_style(config.theme))
        
        # 滚动区域的内容部件
        self.scroll_content = QWidget()
        self.grid_layout = QGridLayout(self.scroll_content)
        self.grid_layout.setSpacing(15)  # 调整间距以完整显示6个头像
        self.grid_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        
        scroll_area.setWidget(self.scroll_content)
        content_layout.addWidget(scroll_area)
        
        
    
    def load_avatars(self):
        """加载所有头像"""
        # 获取头像目录
        profiles_dir = get_resource_path("resources/profile")
        
        if not profiles_dir.exists():
            logger.warning("头像目录不存在: %s", profiles_dir)
            # 创建默认测试头像
            self.create_test_avatars()
            return
        
        # 获取所有PNG文件
        png_files = list(profiles_dir.glob("*.png"))
        
        if not png_files:
            logger.warning("没有找到PNG头像文件")
            self.create_test_avatars()
            return
        
        # 清空现有布局
        while self.grid_layout.count():
            item = self.grid_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        
        # 每行显示的头像数量
        columns = 6
        
        # 添加头像项
        for i, avatar_file in enumerate(png_files):
            row = i // columns
            col = i % columns
            
            # 获取文件名（不带扩展名）
            avatar_name = avatar_file.stem
            
            # 创建头像项
            avatar_item = AvatarItem(str(avatar_file), avatar_name)
            avatar_item.clicked.connect(self.on_avatar_clicked)
            
            self.grid_layout.addWidget(avatar_item, row, col)
    
    def create_test_avatars(self):
        """创建测试头像（用于演示）"""
        # 这里可以添加一些测试逻辑，或者显示提示信息
        pass
    

    
    def showEvent(self, event):
        """窗口显示事件，重新应用样式以支持主题切换"""
        super().showEvent(event)
        # 重新应用样式
        self.setStyleSheet(get_dialog_style(config.theme))
        # 重新加载背景图片
        self.background_pixmap = load_background_image(config.theme)
        central = self.centralWidget()
        if isinstance(central, BackgroundWidget):
            central.set_background(self.background_pixmap, config.theme)
        # 刷新头像样式
        self.refresh_avatar_styles()
    
    def refresh_avatar_styles(self):
        """刷新所有头像的样式"""
        for i in range(self.grid_layout.count()):
            item = self.grid_layout.itemAt(i)
            if item and item.widget():
                widget = item.widget()
                if hasattr(widget, 'avatar_container') and widget.avatar_container:
                    # 重置为默认样式
                    widget.avatar_container.setStyleSheet("border: 2px solid #dee2e6; border-radius: 35px;")
                    # 更新文字样式以支持主题切换
                    for child in widget.findChildren(QLabel):
                        if child is not widget.avatar_container:
                            child.setStyleSheet(get_label_style(config.theme))
    
    def on_theme_changed(self, theme):
        """主题切换信号处理"""
        # 重新应用样式
        self.setStyleSheet(get_dialog_style(theme))
        # 重新加载背景图片
        self.background_pixmap = load_background_image(theme)
        central = self.centralWidget()
        if isinstance(central, BackgroundWidget):
            central.set_background(self.background_pixmap, theme)
        # 刷新头像样式
        self.refresh_avatar_styles()
    
    def on_avatar_clicked(self, avatar_path, avatar_name):
        """处理头像项点击事件"""
        # 发送选择信号
        self.avatar_selected.emit(avatar_path, avatar_name)
        
        # 关闭窗口
        self.close()