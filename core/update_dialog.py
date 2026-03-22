from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel, 
                               QPushButton, QWidget, QTextEdit, QScrollArea)
from PySide6.QtCore import Qt
import webbrowser

from core.config import config
from core.styles import get_dialog_style, get_button_style
from core.widgets import BackgroundWidget, load_background_image


class UpdateDialog(QDialog):
    """自定义更新通知对话框"""
    
    def __init__(self, parent=None, update_info=None):
        super().__init__(parent)
        self.update_info = update_info or {}
        self.setModal(True)
        self.setMinimumWidth(500)
        self.setMinimumHeight(400)
        
        # 设置无边框窗口和透明背景以实现圆角
        self.setWindowFlags(Qt.WindowType.Dialog | Qt.WindowType.FramelessWindowHint)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setStyleSheet(get_dialog_style(config.theme))
        
        # 加载背景图片
        self.background_pixmap = load_background_image(config.theme)
        
        self.init_ui()
    
    def filter_download_content(self, content):
        """过滤掉更新内容中的下载部分"""
        # 查找"## 下载"的位置
        download_start = content.find("## 下载")
        if download_start != -1:
            # 截取下载部分之前的内容
            filtered_content = content[:download_start].strip()
        else:
            # 如果没有找到"## 下载"，返回原内容
            filtered_content = content.strip()
        
        # 清理末尾可能的空行
        filtered_content = filtered_content.rstrip()
        
        return filtered_content if filtered_content else "暂无更新说明"
    
    def init_ui(self):
        """初始化UI"""
        # 创建主布局（透明）
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)
        
        # 创建带背景图片的容器
        self.container_widget = BackgroundWidget(self.background_pixmap, config.theme)
        self.container_widget.setObjectName("dialogContainer")
        container_layout = QVBoxLayout(self.container_widget)
        container_layout.setContentsMargins(0, 0, 0, 0)
        container_layout.setSpacing(0)
        main_layout.addWidget(self.container_widget)
        
        # 添加自定义标题栏
        from core.custom_title_bar import CustomTitleBar
        self.title_bar = CustomTitleBar(self)
        container_layout.addWidget(self.title_bar)
        
        # 内容区域
        content_widget = QWidget()
        layout = QVBoxLayout(content_widget)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(15)
        container_layout.addWidget(content_widget)
        

        
        # 版本信息
        current_version = self.update_info.get('current_version', '')
        latest_version = self.update_info.get('latest_version', '')
        # 标题
        title_label = QLabel(f"发现新版本！当前版本：{current_version}  →  最新版本：{latest_version}")
        title_label.setStyleSheet("font-size: 12px; font-weight: bold; color: #2c3e50;")
        layout.addWidget(title_label)
        
        # 更新说明（可滚动）
        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        scroll_area.setMinimumHeight(100)
        scroll_area.setMaximumHeight(200)
        
        # 获取GitHub发布说明
        release_info = self.update_info.get('release_info', {})
        raw_release_notes = release_info.get('body', '暂无更新说明')
        
        # 过滤掉下载相关内容
        release_notes = self.filter_download_content(raw_release_notes)
        
        # 创建文本显示区域
        text_widget = QWidget()
        text_layout = QVBoxLayout(text_widget)
        
        notes_label = QLabel(release_notes)
        notes_label.setWordWrap(True)
        notes_label.setStyleSheet(get_dialog_style(config.theme))
        text_layout.addWidget(notes_label)
        
        scroll_area.setWidget(text_widget)
        layout.addWidget(scroll_area)
        
        # 下载信息
        download_info = QLabel("点击确认前往 GitHub Release 下载最新版本。")
        download_info.setStyleSheet("font-size: 13px; color: #e74c3c; margin: 10px 0;")
        layout.addWidget(download_info)
        
        # 按钮区域
        button_layout = QHBoxLayout()
        button_layout.addStretch()

        # 确认按钮
        self.confirm_btn = QPushButton("确认")
        self.confirm_btn.setMinimumWidth(100)
        self.confirm_btn.clicked.connect(self.on_confirm)
        button_layout.addWidget(self.confirm_btn)
        
        # 取消按钮
        self.cancel_btn = QPushButton("取消")
        self.cancel_btn.setMinimumWidth(100)
        self.cancel_btn.clicked.connect(self.reject)
        button_layout.addWidget(self.cancel_btn)

        layout.addLayout(button_layout)
        
        # 应用按钮样式
        self.cancel_btn.setStyleSheet(get_button_style(config.theme))
        self.confirm_btn.setStyleSheet(get_button_style(config.theme))
        
        self.setLayout(main_layout)
    
    def on_confirm(self):
        """确认按钮点击事件"""
        release_info = self.update_info.get('release_info', {})
        url = release_info.get('html_url') or f"https://github.com/{config.github_owner}/{config.github_repo}/releases/latest"
        webbrowser.open(url)
        self.accept()