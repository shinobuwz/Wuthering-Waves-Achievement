from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QTabWidget,
                               QWidget, QLabel, QLineEdit, QPushButton,
                               QDialogButtonBox, QFileDialog, QGroupBox, QCheckBox, QTableWidget,
                               QTableWidgetItem, QComboBox, QMessageBox)
from PySide6.QtGui import QColor
from PySide6.QtCore import Qt, Signal, QObject
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWebEngineCore import QWebEngineProfile, QWebEngineUrlRequestInterceptor
import logging

logger = logging.getLogger(__name__)

from core.config import config
from core.draggable_table import DraggableTableWidget
from core.signal_bus import signal_bus
from core.styles import (get_dialog_style, get_settings_desc_style, get_button_style)
from core.custom_message_box import CustomMessageBox
from core.widgets import BackgroundWidget, load_background_image


class AuthInterceptor(QWebEngineUrlRequestInterceptor):
    """拦截 kurobbs 请求头中的 devcode 和 token"""
    credentials_captured = Signal(str, str)  # (devcode, token)

    def interceptRequest(self, info):
        headers = {k.data(): v.data() for k, v in info.httpHeaders().items()}
        devcode = headers.get(b"devcode", b"").decode("utf-8", errors="ignore")
        token = headers.get(b"token", b"").decode("utf-8", errors="ignore")
        logger.debug("[AuthInterceptor] url=%s devcode=%s token=%s",
                     info.requestUrl().toString(),
                     devcode[:8] + "..." if devcode else "(空)",
                     token[:8] + "..." if token else "(空)")
        if devcode and token:
            logger.info("[AuthInterceptor] 成功捕获凭据 devcode=%s... token=%s...",
                        devcode[:8], token[:8])
            self.credentials_captured.emit(devcode, token)


class BrowserAuthDialog(QDialog):
    """内嵌浏览器，自动拦截 kurobbs devcode/token"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("自动获取认证信息 - 请在浏览器中登录库街区")
        self.resize(1024, 700)
        self.devcode = ""
        self.token = ""

        layout = QVBoxLayout(self)
        layout.setContentsMargins(4, 4, 4, 4)

        hint = QLabel("请在下方浏览器中完成登录，登录成功后将自动获取认证信息并关闭此窗口。")
        hint.setWordWrap(True)
        layout.addWidget(hint)

        # 独立命名 profile，保持登录态
        self._profile = QWebEngineProfile("kurobbs_auth", self)
        self._interceptor = AuthInterceptor(self)
        self._interceptor.credentials_captured.connect(self._on_captured)
        self._profile.setUrlRequestInterceptor(self._interceptor)

        self._view = QWebEngineView(self)
        page_cls = self._view.page().__class__
        from PySide6.QtWebEngineCore import QWebEnginePage
        self._page = QWebEnginePage(self._profile, self)
        self._view.setPage(self._page)
        self._view.load("https://www.kurobbs.com/")
        layout.addWidget(self._view)

    def _on_captured(self, devcode, token):
        self.devcode = devcode
        self.token = token
        self.accept()


class TemplateSettingsDialog(QDialog):
    """模板设置对话框"""

    def __init__(self, parent=None):
        super().__init__(parent)

        self.setWindowTitle("设置")
        self.setModal(False)  # 改为非模态，允许用户与其他窗口交互
        self.setFixedSize(850, 600)
        
        # 设置无边框窗口和透明背景以实现圆角
        self.setWindowFlags(Qt.WindowType.Window | Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        
        self.setStyleSheet(get_dialog_style(config.theme))

        # 背景图片初始化
        self.background_pixmap = None
        self._load_background_image()

        self._init_ui()
        self._load_current_settings()

    def _init_ui(self):
        """初始化UI"""
        # 创建主布局（透明）
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)
        
        # 创建容器（用于绘制背景）
        self.container_widget = BackgroundWidget(self.background_pixmap, config.theme)
        self.container_widget.setObjectName("dialogContainer")
        container_layout = QVBoxLayout(self.container_widget)
        container_layout.setContentsMargins(0, 0, 0, 0)
        container_layout.setSpacing(0)
        main_layout.addWidget(self.container_widget)
        
        # 添加自定义标题栏（默认不显示主题切换按钮）
        from core.custom_title_bar import CustomTitleBar
        self.title_bar = CustomTitleBar(self, show_theme_toggle=False)
        container_layout.addWidget(self.title_bar)
        
        # 内容区域
        content_widget = QWidget()
        layout = QVBoxLayout(content_widget)
        container_layout.addWidget(content_widget)

        # 创建标签页
        self.tab_widget = QTabWidget()

        # 添加标签页
        tabs = [
            ("👤 用户管理", self._create_user_tab),
            ("🎨 外观设置", self._create_appearance_tab),
            ("📂 分类管理", self._create_category_tab),
            ("🎯 多选一管理", self._create_achievement_group_tab),
            ("🗂️ 版本管理", self._create_version_tab)
        ]

        for name, creator in tabs:
            self.tab_widget.addTab(creator(), name)

        layout.addWidget(self.tab_widget)
        
        # 连接tab切换事件
        self.tab_widget.currentChanged.connect(self._on_tab_changed)

        # 按钮区域
        button_box = self._create_button_box()
        layout.addWidget(button_box)

        self.setLayout(main_layout)

    def _create_button_box(self):
        """创建按钮区域"""
        button_box = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Save |
            QDialogButtonBox.StandardButton.Cancel
        )

        save_btn = button_box.button(QDialogButtonBox.StandardButton.Save)
        cancel_btn = button_box.button(QDialogButtonBox.StandardButton.Cancel)

        save_btn.setText("保存")
        cancel_btn.setText("取消")
        cancel_btn.setStyleSheet(get_button_style(config.theme))

        button_box.accepted.connect(self._save_settings)
        button_box.rejected.connect(self.reject)

        return button_box

    def _create_appearance_tab(self) -> QWidget:
        """创建外观设置标签页"""
        widget = QWidget()
        layout = QVBoxLayout(widget)

        # 背景图片设置组
        bg_group = QGroupBox("背景图片设置")
        bg_layout = QVBoxLayout(bg_group)

        # 是否使用背景图片
        use_bg_layout = QHBoxLayout()
        self.use_background_checkbox = QCheckBox("使用背景图片")
        self.use_background_checkbox.setChecked(config.use_background)
        self.use_background_checkbox.setToolTip("关闭后将使用纯色背景")
        use_bg_layout.addWidget(self.use_background_checkbox)
        use_bg_layout.addStretch()
        bg_layout.addLayout(use_bg_layout)

        # 浅色模式背景图片
        light_bg_layout = QHBoxLayout()
        light_bg_layout.addWidget(QLabel("浅色背景:"))
        self.custom_bg_light_edit = QLineEdit()
        self.custom_bg_light_edit.setPlaceholderText("留空使用默认背景...")
        self.custom_bg_light_edit.setText(config.custom_background_light)
        self.custom_bg_light_edit.setReadOnly(True)
        light_bg_layout.addWidget(self.custom_bg_light_edit)

        select_light_btn = QPushButton("📁 选择")
        select_light_btn.clicked.connect(lambda: self._select_background_image("light"))
        light_bg_layout.addWidget(select_light_btn)

        clear_light_btn = QPushButton("🗑️")
        clear_light_btn.setStyleSheet(get_button_style(config.theme))
        clear_light_btn.clicked.connect(lambda: self.custom_bg_light_edit.clear())
        light_bg_layout.addWidget(clear_light_btn)

        bg_layout.addLayout(light_bg_layout)

        # 深色模式背景图片
        dark_bg_layout = QHBoxLayout()
        dark_bg_layout.addWidget(QLabel("深色背景:"))
        self.custom_bg_dark_edit = QLineEdit()
        self.custom_bg_dark_edit.setPlaceholderText("留空使用默认背景...")
        self.custom_bg_dark_edit.setText(config.custom_background_dark)
        self.custom_bg_dark_edit.setReadOnly(True)
        dark_bg_layout.addWidget(self.custom_bg_dark_edit)

        select_dark_btn = QPushButton("📁 选择")
        select_dark_btn.clicked.connect(lambda: self._select_background_image("dark"))
        dark_bg_layout.addWidget(select_dark_btn)

        clear_dark_btn = QPushButton("🗑️")
        clear_dark_btn.setStyleSheet(get_button_style(config.theme))
        clear_dark_btn.clicked.connect(lambda: self.custom_bg_dark_edit.clear())
        dark_bg_layout.addWidget(clear_dark_btn)

        bg_layout.addLayout(dark_bg_layout)

        # 背景图片说明
        bg_help = QLabel(
            "背景图片说明：\n"
            "• 关闭背景图片后将使用纯色背景\n"
            "• 可以分别为浅色和深色模式设置不同的背景图片\n"
            "• 支持 PNG、JPG、JPEG 格式\n"
            "• 留空则使用内置的默认背景图片"
        )
        bg_help.setStyleSheet(get_settings_desc_style(config.theme))
        bg_help.setWordWrap(True)
        bg_layout.addWidget(bg_help)

        layout.addWidget(bg_group)
        layout.addStretch()

        return widget

    def _create_version_tab(self) -> QWidget:
        """创建版本管理标签页"""
        widget = QWidget()
        layout = QVBoxLayout(widget)

        # 添加说明文字
        desc_label = QLabel(
            "📖 <b>版本数据管理：</b><br>"
            "选择要删除的版本号，系统会在删除前自动导出当前所有成就数据作为备份。<br>"
            "<span style='color: #e74c3c;'><b>⚠️ 警告：</b></span>删除操作不可恢复，请谨慎操作！"
        )
        desc_label.setWordWrap(True)
        desc_label.setStyleSheet(get_settings_desc_style(config.theme))
        layout.addWidget(desc_label)

        # 版本选择组
        version_group = QGroupBox("版本删除")
        version_layout = QVBoxLayout(version_group)

        # 版本选择和按钮布局（一行）
        version_controls_layout = QHBoxLayout()
        version_controls_layout.addWidget(QLabel("选择版本:"))
        self.version_combo = QComboBox()
        self.version_combo.setMinimumWidth(200)
        version_controls_layout.addWidget(self.version_combo)
        
        # 添加弹性空间
        version_controls_layout.addStretch()
        
        # 三个按钮
        self.refresh_versions_btn = QPushButton("刷新版本列表")
        self.refresh_versions_btn.setStyleSheet(get_button_style(config.theme))
        self.refresh_versions_btn.clicked.connect(self.refresh_version_list)
        version_controls_layout.addWidget(self.refresh_versions_btn)
        
        self.delete_version_btn = QPushButton("删除选定版本")
        self.delete_version_btn.setStyleSheet(get_button_style(config.theme))
        self.delete_version_btn.clicked.connect(self.delete_version_data)
        version_controls_layout.addWidget(self.delete_version_btn)
        
        version_layout.addLayout(version_controls_layout)

        layout.addWidget(version_group)
        layout.addStretch()

        # 初始化版本列表
        self.refresh_version_list()

        return widget

    def _create_help_tab(self) -> QWidget:
        """创建帮助标签页"""
        from PySide6.QtWidgets import QScrollArea
        
        widget = QWidget()
        layout = QVBoxLayout(widget)
        layout.setContentsMargins(0, 0, 0, 0)
        
        scroll_area = QScrollArea()
        scroll_area.setWidgetResizable(True)
        scroll_area.setFrameShape(QScrollArea.Shape.NoFrame)
        from core.styles import get_scroll_area_style
        scroll_area.setStyleSheet(get_scroll_area_style(config.theme))
        
        scroll_content = QWidget()
        scroll_content.setStyleSheet("QWidget { background: transparent; }")
        scroll_layout = QVBoxLayout(scroll_content)

        help_group = QGroupBox("使用指南")
        help_group.setStyleSheet("QGroupBox { background: transparent; }")
        help_layout = QVBoxLayout(help_group)

        help_text = QLabel(
            "<h3>鸣潮成就管理器 - 使用帮助</h3>"
            "<p><b>1. 通用认证信息设置</b></p>"
            "<p style='margin-left: 20px;'>在使用爬虫功能前，需要先在<b>爬虫</b>标签页中设置通用认证信息（devcode 和 token）。"
            "这些信息用于访问库街区Wiki API获取成就数据。</p>"
            
            "<p><b>2. 旧数据迁移指南</b></p>"
            "<p style='margin-left: 20px;'>如果您之前使用HTML版本的成就管理工具：<br>"
            "① 在旧版本中使用<b>导出JSON</b>功能导出您的成就数据<br>"
            "② 在本应用的<b>成就管理</b>标签页中点击<b>导入JSON</b>按钮<br>"
            "③ 选择导出的JSON文件即可恢复您的成就进度</p>"
            
            "<p><b>3. 数据版本说明</b></p>"
            "<p style='margin-left: 20px;'>当前应用内置了<b>1.0-2.8版本</b>的完整成就数据，共 764 条。<br>"
            "<span style='color: #e74c3c;'><b>⚠️ 重要提示：</b></span>不建议使用爬虫功能爬取旧版本数据覆盖现有数据，"
            "因为库街区Wiki的源数据存在以下问题：<br>"
            "• &nbsp;&nbsp;&nbsp;多了一条不存在的成就：要用声骸打败声骸<br>"
            "• 少了几条实际存在的成就：人形定风珠、战迹如新、大斩龙屠、失色的深红、江湖路远、凭一口气，点一盏灯<br>"
            "&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;为荣耀倾注的花雨、他们都叫我残像收割机、无欲无求的我很难理解<br>"
            "建议仅在新版本发布后使用爬虫功能更新数据。</p>"
            
            "<p><b>4. 爬虫使用说明</b></p>"
            "<p style='margin-left: 20px;'>爬虫功能<b>仅支持单个版本</b>的数据爬取。<br>"
            "使用步骤：<br>"
            "① 在爬虫标签页设置通用认证信息<br>"
            "② 选择要爬取的版本（如：2.9）<br>"
            "③ 点击开始爬取按钮<br>"
            "④ 等待爬取完成后保存数据<br><br>"
            "<b>缓存机制：</b><br>"
            "• 首次爬取时会将网页数据保存到本地缓存（resources/achievement_cache.json）<br>"
            "• 下次爬取时会优先使用本地缓存，无需重新请求网络<br>"
            "• 点击<b>清除缓存</b>按钮可删除本地缓存文件，下次爬取将重新获取最新数据<br>"
            "• 点击<b>打开WIKI</b>按钮可在浏览器中查看库街区Wiki成就页面</p>"
            
            "<p><b>5. 状态列操作说明</b></p>"
            "<p style='margin-left: 20px;'>在成就管理标签页的表格中：<br>"
            "• <b>单击</b>状态列：在<span style='color: #27ae60;'>已完成</span>和<span style='color: #95a5a6;'>未完成</span>之间切换<br>"
            "• <b>长按</b>状态列（按住1秒）：切换为<span style='color: #e67e22;'>暂不可获取</span>状态<br>"
            "• 再次单击可恢复为未完成状态</p>"
            
            "<p><b>6. 资源获取方式</b></p>"
            "<p style='margin-left: 20px;'>如需添加更多角色头像和肖像图资源：</p>"
            "<p style='margin-left: 40px;'><b>头像图片：</b><br>"
            "① 访问 <a href='https://wiki.kurobbs.com/mc/catalogue/list?fid=1099&sid=1363' style='color: #3498db; text-decoration: underline;'>库街区Wiki-角色头像页面</a><br>"
            "② 直接拖动每个角色的头像图片到 <code>resources\\profile</code> 文件夹<br>"
            "③ 将图片重命名为角色名（如：今汐.png）<br>"
            "<p style='margin-left: 20px;'><span style='color: #3498db;'><b>💡 提示：</b></span>"
            "在主窗口点击头像切换头像，会自动更新同角色肖像图。</p>"
            "<p style='margin-left: 40px;'><b>角色肖像图：</b><br>"
            "① 访问 <a href='https://wiki.kurobbs.com/mc/catalogue/list?fid=1099&sid=1105' style='color: #3498db; text-decoration: underline;'>库街区Wiki-角色列表页面</a><br>"
            "② 点击每个角色进入详情页<br>"
            "③ 拖动角色的全身肖像图到 <code>resources\\characters</code> 文件夹<br>"
            "④ 将图片重命名为角色名（如：今汐.webp）<br>"
            "<p style='margin-left: 20px;'><span style='color: #3498db;'><b>💡 提示：</b></span>"
            "头像和肖像图的文件名必须与角色名完全一致，这样切换头像时才能自动联动显示对应的肖像图。<s>缄默</s></p>"
        )
        help_text.setWordWrap(True)
        help_text.setTextFormat(Qt.TextFormat.RichText)
        help_text.setOpenExternalLinks(True)
        help_text.setStyleSheet("QLabel a { color: #3498db; text-decoration: underline; }")
        help_layout.addWidget(help_text)

        scroll_layout.addWidget(help_group)
        scroll_layout.addStretch()
        
        scroll_area.setWidget(scroll_content)
        layout.addWidget(scroll_area)

        return widget

    def _select_background_image(self, theme_mode):
        """选择背景图片"""
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            f"选择{'浅色' if theme_mode == 'light' else '深色'}模式背景图片",
            "",
            "图片文件 (*.png *.jpg *.jpeg)"
        )
        
        if file_path:
            if theme_mode == "light":
                self.custom_bg_light_edit.setText(file_path)
            else:
                self.custom_bg_dark_edit.setText(file_path)

    def _load_current_settings(self):
        """加载当前设置"""
        # 外观设置
        self.use_background_checkbox.setChecked(config.use_background)
        self.custom_bg_light_edit.setText(config.custom_background_light)
        self.custom_bg_dark_edit.setText(config.custom_background_dark)
        
        # 通用认证设置
        self.devcode_edit.setText(config.devcode)
        self.token_edit.setText(config.token)

    def _on_auto_fetch(self):
        """打开内嵌浏览器自动获取 devcode/token"""
        dlg = BrowserAuthDialog(self)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            self.devcode_edit.setText(dlg.devcode)
            self.token_edit.setText(dlg.token)

    def _save_settings(self):
        """保存设置"""
        # 保存外观设置
        config.use_background = self.use_background_checkbox.isChecked()
        config.custom_background_light = self.custom_bg_light_edit.text()
        config.custom_background_dark = self.custom_bg_dark_edit.text()
        
        # 保存通用认证设置
        config.devcode = self.devcode_edit.text().strip()
        config.token = self.token_edit.text().strip()
        
        # 保存分类配置
        self._save_category_config_silent()
        
        # 保存到配置文件
        config.save_to_settings()
        
        # 发射设置变更信号（包含所有设置）
        signal_bus.settings_changed.emit({
            'use_background': config.use_background,
            'custom_background_light': config.custom_background_light,
            'custom_background_dark': config.custom_background_dark,
            'devcode': config.devcode,
            'token': config.token,
            'theme': config.theme
        })
        
        # CustomMessageBox.information(self, "成功", "设置已保存")
        self.accept()

    def _create_user_tab(self):
        """创建用户管理标签页"""
        widget = QWidget()
        layout = QVBoxLayout(widget)

        # 添加获取认证信息的提示
        help_label = QLabel(
            "📖 <b>如何获取认证信息：</b><br>"
            "1. 访问库街区首页：<a href='https://www.kurobbs.com/' style='color: #0078d4;'>https://www.kurobbs.com/</a><br>"
            "2. 登录后，按 <b>F12</b> 打开开发者工具（Developer Tools）<br>"
            "3. 切换到 <b>网络</b>（Network）标签<br>"
            "4. 刷新页面（F5 或 Ctrl+R）<br>"
            "5. 在请求列表中找到名称为 <b>getConfig</b> 的请求<br>"
            "6. 点击该请求，在右侧切换到标头标签，滚动查看 <b>请求标头</b>（Request Headers）<br>"
            "7. 找到 <b>Devcode</b> 和 <b>Token</b> 字段，复制其值到下方输入框"
        )
        help_label.setWordWrap(True)
        help_label.setOpenExternalLinks(True)  # 允许点击链接
        help_label.setStyleSheet(get_settings_desc_style(config.theme))
        layout.addWidget(help_label)

        # 通用认证设置
        auth_group = QGroupBox("通用认证设置")
        auth_layout = QVBoxLayout(auth_group)

        # DevCode输入
        devcode_layout = QHBoxLayout()
        devcode_layout.addWidget(QLabel("DevCode:"))
        self.devcode_edit = QLineEdit()
        self.devcode_edit.setPlaceholderText("输入DevCode")
        self.devcode_edit.setText(config.devcode)
        self.devcode_edit.setEchoMode(QLineEdit.EchoMode.Password)  # 密文显示
        devcode_layout.addWidget(self.devcode_edit)
        auth_layout.addLayout(devcode_layout)

        # Token输入
        token_layout = QHBoxLayout()
        token_layout.addWidget(QLabel("Token:"))
        self.token_edit = QLineEdit()
        self.token_edit.setPlaceholderText("输入Token")
        self.token_edit.setText(config.token)
        self.token_edit.setEchoMode(QLineEdit.EchoMode.Password)  # 密文显示
        token_layout.addWidget(self.token_edit)
        auth_layout.addLayout(token_layout)

        # 自动获取按钮
        auto_fetch_layout = QHBoxLayout()
        self.auto_fetch_btn = QPushButton("🔑 自动获取（内嵌浏览器登录）")
        self.auto_fetch_btn.clicked.connect(self._on_auto_fetch)
        auto_fetch_layout.addWidget(self.auto_fetch_btn)
        auto_fetch_layout.addStretch()
        auth_layout.addLayout(auto_fetch_layout)

        layout.addWidget(auth_group)

        # 当前用户信息
        current_group = QGroupBox("当前用户")
        current_layout = QVBoxLayout(current_group)

        self.current_user_label = QLabel(f"当前用户: {config.get_current_user() or '未设置'}")
        current_layout.addWidget(self.current_user_label)

        layout.addWidget(current_group)

        # 创建水平布局：左侧添加用户，右侧用户列表
        user_main_layout = QHBoxLayout()
        
        # 左侧：添加用户
        add_group = QGroupBox("添加用户")
        add_layout = QVBoxLayout(add_group)

        # 昵称输入
        nickname_layout = QHBoxLayout()
        nickname_layout.addWidget(QLabel("昵称:"))
        self.nickname_edit = QLineEdit()
        self.nickname_edit.setPlaceholderText("输入昵称")
        nickname_layout.addWidget(self.nickname_edit)
        add_layout.addLayout(nickname_layout)

        # UID输入
        uid_layout = QHBoxLayout()
        uid_layout.addWidget(QLabel("UID:"))
        self.uid_edit = QLineEdit()
        self.uid_edit.setPlaceholderText("输入UID")
        uid_layout.addWidget(self.uid_edit)
        add_layout.addLayout(uid_layout)

        # 添加按钮
        self.add_user_btn = QPushButton("添加用户")
        self.add_user_btn.clicked.connect(self._add_user)
        add_layout.addWidget(self.add_user_btn)
        
        add_layout.addStretch()
        user_main_layout.addWidget(add_group, 1)  # 左侧占1份

        # 右侧：用户列表
        list_group = QGroupBox("用户列表")
        list_layout = QVBoxLayout(list_group)

        from PySide6.QtWidgets import QTableWidget
        self.user_table = QTableWidget()
        self.user_table.setColumnCount(4)
        self.user_table.setHorizontalHeaderLabels(["昵称", "UID", "切换", "删除"])
        self.user_table.horizontalHeader().setStretchLastSection(True)
        self.user_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.user_table.setSelectionMode(QTableWidget.SelectionMode.NoSelection)  # 禁止选择
        self.user_table.setFocusPolicy(Qt.FocusPolicy.NoFocus)  # 去掉焦点框
        self.user_table.setShowGrid(True)  # 显示网格线
        
        # 应用滚动条样式
        from core.styles import get_scrollbar_style
        self.user_table.setStyleSheet(self.user_table.styleSheet() + get_scrollbar_style(config.theme))
        
        # 设置列宽
        self.user_table.setColumnWidth(0, 120)  # 昵称
        self.user_table.setColumnWidth(1, 120)  # UID
        self.user_table.setColumnWidth(2, 60)   # 切换
        self.user_table.setColumnWidth(3, 60)   # 删除
        
        # 监听单元格点击事件
        self.user_table.cellClicked.connect(self._on_cell_clicked)
        # 监听单元格编辑完成事件
        self.user_table.itemChanged.connect(self._on_nickname_changed)
        
        self._refresh_user_list()
        
        list_layout.addWidget(self.user_table)
        user_main_layout.addWidget(list_group, 2)  # 右侧占2份
        
        layout.addLayout(user_main_layout)
        layout.addStretch()
        return widget

    def _refresh_user_list(self):
        """刷新用户列表"""
        from PySide6.QtWidgets import QTableWidgetItem
        from PySide6.QtGui import QColor
        from PySide6.QtCore import Qt
        
        # 暂时断开信号，避免刷新时触发编辑事件
        self.user_table.itemChanged.disconnect(self._on_nickname_changed)
        
        # 清除表格
        self.user_table.setRowCount(0)
        
        # 添加用户数据
        users = config.get_users()
        current_user = config.get_current_user()
        
        for row, (username, user_data) in enumerate(users.items()):
            self.user_table.insertRow(row)
            
            # 显示昵称和UID
            nickname = user_data.get('nickname', username) if isinstance(user_data, dict) else username
            uid = user_data.get('uid', '') if isinstance(user_data, dict) else ''
            
            # 昵称列（可编辑）
            nickname_item = QTableWidgetItem(nickname)
            nickname_item.setData(Qt.ItemDataRole.UserRole, username)  # 保存username用于后续操作
            if username == current_user:
                nickname_item.setForeground(QColor(0, 120, 212))  # 蓝色
                font = nickname_item.font()
                font.setBold(True)
                nickname_item.setFont(font)
            self.user_table.setItem(row, 0, nickname_item)
            
            # UID列（不可编辑）
            uid_item = QTableWidgetItem(uid)
            uid_item.setFlags(uid_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
            self.user_table.setItem(row, 1, uid_item)
            
            # 切换列（文字）
            if username != current_user:
                switch_item = QTableWidgetItem("切换")
                switch_item.setForeground(QColor(0, 120, 212))  # 蓝色
                switch_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
                switch_item.setFlags(switch_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
                switch_item.setData(Qt.ItemDataRole.UserRole, username)
                self.user_table.setItem(row, 2, switch_item)
            else:
                empty_item = QTableWidgetItem("")
                empty_item.setFlags(empty_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
                self.user_table.setItem(row, 2, empty_item)
            
            # 删除列（文字）
            delete_item = QTableWidgetItem("删除")
            delete_item.setForeground(QColor(220, 53, 69))  # 红色
            delete_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            delete_item.setFlags(delete_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
            delete_item.setData(Qt.ItemDataRole.UserRole, username)
            self.user_table.setItem(row, 3, delete_item)
        
        # 重新连接信号
        self.user_table.itemChanged.connect(self._on_nickname_changed)

    def _add_user(self):
        """添加用户"""
        nickname = self.nickname_edit.text().strip()
        uid = self.uid_edit.text().strip()
        
        if not nickname:
            CustomMessageBox.warning(self, "警告", "请输入昵称")
            return
        
        if not uid:
            CustomMessageBox.warning(self, "警告", "请输入UID")
            return
        
        # 检查UID是否已存在
        users = config.get_users()
        for user_data in users.values():
            if user_data.get('uid', '') == uid:
                CustomMessageBox.warning(self, "警告", "UID已存在")
                return
        
        # 添加用户（昵称作为键，包含UID信息）
        user_data = {'nickname': nickname, 'uid': uid}
        config.add_user(nickname, user_data)
        
        # 清空输入框
        self.nickname_edit.clear()
        self.uid_edit.clear()
        
        # 更新当前用户标签
        current_user = config.get_current_user()
        current_data = users.get(current_user, {})
        current_nickname = current_data.get('nickname', current_user)
        self.current_user_label.setText(f"当前用户: {current_nickname}")
        
        # 刷新用户列表
        self._refresh_user_list()

    def _switch_to_user(self, username):
        """切换到指定用户"""
        if config.switch_user(username):
            # 更新当前用户标签
            self.current_user_label.setText(f"当前用户: {config.get_current_user()}")
            
            # 刷新用户列表
            self._refresh_user_list()
        else:
            CustomMessageBox.warning(self, "警告", "切换用户失败")

    def _on_cell_clicked(self, row, column):
        """处理单元格点击事件"""
        from PySide6.QtCore import Qt
        
        if column == 2:  # 切换列
            item = self.user_table.item(row, column)
            if item and item.text() == "切换":
                username = item.data(Qt.ItemDataRole.UserRole)
                self._switch_to_user(username)
        elif column == 3:  # 删除列
            item = self.user_table.item(row, column)
            if item and item.text() == "删除":
                username = item.data(Qt.ItemDataRole.UserRole)
                self._delete_user(username)
    
    def _on_nickname_changed(self, item):
        """昵称编辑完成"""
        if item.column() == 0:  # 昵称列
            from PySide6.QtCore import Qt
            username = item.data(Qt.ItemDataRole.UserRole)
            new_nickname = item.text().strip()
            
            if not new_nickname:
                CustomMessageBox.warning(self, "警告", "昵称不能为空")
                self._refresh_user_list()
                return
            
            # 更新用户昵称
            if username in config.users:
                if isinstance(config.users[username], dict):
                    config.users[username]['nickname'] = new_nickname
                else:
                    config.users[username] = {'nickname': new_nickname, 'uid': ''}
                
                # 保存配置
                config.save_config()
                
                # 刷新用户列表
                self._refresh_user_list()
                
                # 如果是当前用户，更新显示
                if username == config.get_current_user():
                    self.current_user_label.setText(f"当前用户: {new_nickname}")
    
    def _delete_user(self, username):
        """删除用户"""
        reply = CustomMessageBox.question(
            self, "确认", f"确定要删除用户 {username} 吗？", 
            ("是", "否")
        )
        
        if reply == CustomMessageBox.Yes:
            # 从用户列表中删除
            if username in config.users:
                # 获取用户的UID用于删除存档文件
                user_data = config.users[username]
                uid = user_data.get('uid', username) if isinstance(user_data, dict) else username
                
                del config.users[username]
                
                # 同时删除用户的头像和角色名信息
                if username in config.user_avatars:
                    del config.user_avatars[username]
                if username in config.user_character_names:
                    del config.user_character_names[username]
                
                # 删除用户的存档文件
                import os
                from core.config import get_resource_path
                progress_file = get_resource_path(f"resources/user_progress_{uid}.json")
                if progress_file.exists():
                    try:
                        os.remove(progress_file)
                        logger.info("已删除用户 %s (UID: %s) 的存档文件", username, uid)
                    except Exception as e:
                        logger.error("删除用户存档文件失败: %s", str(e))
                
                # 如果删除的是当前用户，需要切换到第一个用户
                if username == config.get_current_user():
                    if config.users:  # 如果还有其他用户
                        # 获取第一个用户
                        first_user = list(config.users.keys())[0]
                        # 使用switch_user方法切换，会自动发射信号
                        config.switch_user(first_user)
                        # 获取第一个用户的昵称
                        first_user_data = config.users[first_user]
                        if isinstance(first_user_data, dict):
                            nickname = first_user_data.get('nickname', first_user)
                        else:
                            nickname = first_user_data
                        self.current_user_label.setText(f"当前用户: {nickname}")
                    else:  # 如果没有其他用户了
                        config.current_user = ""
                        self.current_user_label.setText("当前用户: 未设置")
                
                # 保存配置
                config.save_config()
                
                # 刷新用户列表
                self._refresh_user_list()
    
    def _create_category_tab(self):
        """创建分类管理标签页"""
        from core.config import config
        category_config = config.load_category_config()
        
        widget = QWidget()
        layout = QVBoxLayout(widget)
        
        # 说明文字
        info_label = QLabel("管理成就分类配置，用于智能编号分配")
        info_label.setStyleSheet(get_settings_desc_style(config.theme))
        layout.addWidget(info_label)
        
        # 创建左右布局容器
        categories_layout = QHBoxLayout()
        
        # 第一分类管理（左侧）
        first_group = QGroupBox("第一分类")
        first_layout = QVBoxLayout(first_group)
        
        # 第一分类表格
        self.first_category_table = DraggableTableWidget()
        self.first_category_table.setColumnCount(1)
        self.first_category_table.setHorizontalHeaderLabels(["分类名称"])
        self.first_category_table.horizontalHeader().setStretchLastSection(True)
        
        # 应用滚动条样式
        from core.styles import get_scrollbar_style
        self.first_category_table.setStyleSheet(self.first_category_table.styleSheet() + get_scrollbar_style(config.theme))
        
        # 填充第一分类数据
        first_categories = category_config.get("first_categories", {})
        # 按排序值排序后显示
        sorted_categories = sorted(first_categories.items(), key=lambda x: x[1])
        self.first_category_table.setRowCount(len(sorted_categories))
        for i, (name, order) in enumerate(sorted_categories):
            self.first_category_table.setItem(i, 0, QTableWidgetItem(name))
        
        # 设置表格最小高度
        self.first_category_table.setMinimumHeight(400)
        first_layout.addWidget(self.first_category_table)
        
        # 第一分类按钮
        first_btn_layout = QHBoxLayout()
        first_btn_layout.addStretch()  # 左侧弹性空间
        add_first_btn = QPushButton("添加")
        add_first_btn.setFixedWidth(80)  # 4字宽度
        add_first_btn.clicked.connect(self._add_first_category_row)
        delete_first_btn = QPushButton("删除")
        delete_first_btn.setFixedWidth(80)  # 4字宽度
        delete_first_btn.clicked.connect(self._delete_first_category)
        first_btn_layout.addWidget(add_first_btn)
        first_btn_layout.addWidget(delete_first_btn)
        first_btn_layout.addStretch()  # 右侧弹性空间
        first_layout.addLayout(first_btn_layout)
        
        categories_layout.addWidget(first_group)
        
        # 第二分类管理（右侧）
        second_group = QGroupBox("第二分类")
        second_layout = QVBoxLayout(second_group)
        
        # 第一分类选择器
        first_selector_layout = QHBoxLayout()
        first_selector_layout.addWidget(QLabel("选择第一分类:"))
        self.first_category_combo = QComboBox()
        self.first_category_combo.addItems(list(first_categories.keys()))
        self.first_category_combo.currentTextChanged.connect(self._load_second_categories)
        first_selector_layout.addWidget(self.first_category_combo)
        first_selector_layout.addStretch()
        second_layout.addLayout(first_selector_layout)
        
        # 第二分类表格
        self.second_category_table = DraggableTableWidget()
        self.second_category_table.setColumnCount(1)
        self.second_category_table.setHorizontalHeaderLabels(["分类名称"])
        self.second_category_table.horizontalHeader().setStretchLastSection(True)
        
        # 应用滚动条样式
        from core.styles import get_scrollbar_style
        self.second_category_table.setStyleSheet(self.second_category_table.styleSheet() + get_scrollbar_style(config.theme))
        
        # 设置表格最小高度
        self.second_category_table.setMinimumHeight(400)
        second_layout.addWidget(self.second_category_table)
        
        # 第二分类按钮
        second_btn_layout = QHBoxLayout()
        second_btn_layout.addStretch()  # 左侧弹性空间
        add_second_btn = QPushButton("添加")
        add_second_btn.setFixedWidth(80)  # 4字宽度
        add_second_btn.clicked.connect(self._add_second_category_row)
        delete_second_btn = QPushButton("删除")
        delete_second_btn.setFixedWidth(80)  # 4字宽度
        delete_second_btn.clicked.connect(self._delete_second_category)
        second_btn_layout.addWidget(add_second_btn)
        second_btn_layout.addWidget(delete_second_btn)
        second_btn_layout.addStretch()  # 右侧弹性空间
        second_layout.addLayout(second_btn_layout)
        
        categories_layout.addWidget(second_group)
        
        # 设置左右比例：第一分类占1/3，第二分类占2/3
        categories_layout.setStretch(0, 1)  # 第一分类占1份
        categories_layout.setStretch(1, 2)  # 第二分类占2份
        
        layout.addLayout(categories_layout)
        
        # 重新编号按钮区域
        reencode_layout = QHBoxLayout()
        reencode_layout.addStretch()
        reencode_btn = QPushButton("重新编号")
        reencode_btn.setFixedWidth(100)  # 5字宽度
        reencode_btn.clicked.connect(self._reencode_achievements)
        reencode_layout.addWidget(reencode_btn)
        reencode_layout.addStretch()
        layout.addLayout(reencode_layout)
        
        layout.addStretch()  # 添加弹性空间
        
        # 初始化第二分类缓存
        self._second_categories_cache = category_config.get("second_categories", {}).copy()
        
        # 初始加载第二分类
        if first_categories:
            self._load_second_categories()
        
        # 连接行移动信号
        self.first_category_table._on_row_moved = lambda table, src, dst: self._on_first_category_row_moved(src, dst)
        self.second_category_table._on_row_moved = lambda table, src, dst: self._on_second_category_row_moved(src, dst)
        
        return widget
    
    def _create_achievement_group_tab(self):
        """创建成就组管理标签页"""
        from core.config import config
        
        widget = QWidget()
        layout = QVBoxLayout(widget)
        
        # 说明文字
        info_label = QLabel("管理多选一配置，设置多选一成就关系")
        info_label.setStyleSheet(get_settings_desc_style(config.theme))
        layout.addWidget(info_label)
        
        # 创建左右布局容器
        groups_layout = QHBoxLayout()
        
        # 成就组管理（左侧）- 调整宽度
        groups_group = QGroupBox("成就组管理")
        groups_group.setMaximumWidth(350)  # 增加最大宽度
        groups_layout_widget = QVBoxLayout(groups_group)
        
        # 成就组表格
        self.groups_table = DraggableTableWidget()
        self.groups_table.setColumnCount(1)
        self.groups_table.setHorizontalHeaderLabels(["组名称"])
        self.groups_table.horizontalHeader().setStretchLastSection(True)
        
        # 设置组ID列不可编辑，组名称列可编辑
        
        
        # 应用滚动条样式
        from core.styles import get_scrollbar_style
        self.groups_table.setStyleSheet(self.groups_table.styleSheet() + get_scrollbar_style(config.theme))
        
        # 禁用选择行为
        self.groups_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.groups_table.setSelectionMode(QTableWidget.SelectionMode.NoSelection)
        
        # 禁用焦点以移除焦点指示器
        self.groups_table.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        
        # 填充成就组数据
        self._load_achievement_groups()
        
        # 设置表格最小高度
        self.groups_table.setMinimumHeight(400)
        groups_layout_widget.addWidget(self.groups_table)
        
        # 成就组按钮
        groups_btn_layout = QHBoxLayout()
        groups_btn_layout.addStretch()  # 左侧弹性空间
        
        add_group_btn = QPushButton("添加组")
        add_group_btn.setStyleSheet(get_button_style(config.theme))
        add_group_btn.clicked.connect(self._add_achievement_group)
        groups_btn_layout.addWidget(add_group_btn)
        
        delete_group_btn = QPushButton("删除组")
        delete_group_btn.setStyleSheet(get_button_style(config.theme))
        delete_group_btn.clicked.connect(self._delete_achievement_group)
        groups_btn_layout.addWidget(delete_group_btn)
        
        groups_layout_widget.addLayout(groups_btn_layout)
        groups_layout.addWidget(groups_group, 1)  # 设置占比为1
        
        # 组内成就管理（右侧）- 占据更多空间
        members_group = QGroupBox("组内成就管理")
        members_layout = QVBoxLayout(members_group)
        
        # 当前选择的组
        current_group_layout = QHBoxLayout()
        current_group_layout.addWidget(QLabel("当前组："))
        self.current_group_label = QLabel("未选择")
        self.current_group_label.setStyleSheet("font-weight: bold; color: #0078d4;")
        current_group_layout.addWidget(self.current_group_label)
        current_group_layout.addStretch()
        members_layout.addLayout(current_group_layout)
        
        # 组内成就表格
        self.group_members_table = QTableWidget()
        self.group_members_table.setColumnCount(3)
        self.group_members_table.setHorizontalHeaderLabels(["名称", "描述", "移除"])
        self.group_members_table.horizontalHeader().setStretchLastSection(True)
        self.group_members_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.group_members_table.setSelectionMode(QTableWidget.SelectionMode.NoSelection)  # 禁止选择
        self.group_members_table.setFocusPolicy(Qt.FocusPolicy.StrongFocus)  # 允许获得焦点
        self.group_members_table.setShowGrid(True)  # 显示网格线
        self.group_members_table.setStyleSheet(self.group_members_table.styleSheet() + get_scrollbar_style(config.theme))
        
        # 设置列宽
        self.group_members_table.setColumnWidth(0, 150)  # 名称
        self.group_members_table.setColumnWidth(1, 350)  # 描述 - 宽一些
        self.group_members_table.setColumnWidth(2, 60)   # 移除
        
        # 监听单元格点击事件
        self.group_members_table.cellClicked.connect(self._on_member_cell_clicked)
        
        # 测试连接是否成功
        logger.debug("组内成员表格点击事件已连接，表格行数: %s", self.group_members_table.rowCount())
        
        self.group_members_table.setMinimumHeight(400)  # 增加最小高度
        members_layout.addWidget(self.group_members_table)
        
        # 成就按钮
        members_btn_layout = QHBoxLayout()
        members_btn_layout.addStretch()
        
        add_member_btn = QPushButton("添加成就")
        add_member_btn.setStyleSheet(get_button_style(config.theme))
        add_member_btn.clicked.connect(self._add_group_member)
        members_btn_layout.addWidget(add_member_btn)
        

        
        members_layout.addLayout(members_btn_layout)
        groups_layout.addWidget(members_group, 3)  # 设置占比为3，占据更多空间
        
        layout.addLayout(groups_layout)
        
        # 工具按钮
        tools_layout = QHBoxLayout()
        tools_layout.addStretch()
        

        
        layout.addLayout(tools_layout)
        
        # 连接表格点击事件
        self.groups_table.cellClicked.connect(self._on_group_cell_clicked)
        
        return widget
    
    def _load_achievement_groups(self):
        """加载成就组数据"""
        # 保存当前选中的组ID
        selected_group_id = None
        if hasattr(self, 'groups_table') and self.groups_table.rowCount() > 0:
            current_row = self.groups_table.currentRow()
            if current_row >= 0:
                group_id_item = self.groups_table.item(current_row, 0)
                if group_id_item:
                    selected_group_id = group_id_item.text()
        
        # 获取所有成就数据
        achievements = config.load_base_achievements()
        logger.debug("加载了 %s 个成就数据", len(achievements))
        
        # 收集所有成就组
        groups = {}
        group_count = 0
        for achievement in achievements:
            group_id = achievement.get('成就组ID')
            if group_id:
                group_count += 1
                logger.debug("找到成就组 %s: %s", group_id, achievement.get('名称', ''))
                if group_id not in groups:
                    groups[group_id] = {
                        'id': group_id,
                        'name': self._generate_group_name(group_id),  # 自动生成友好的组名
                        'members': []
                    }
                groups[group_id]['members'].append(achievement)
        
        logger.debug("共找到 %s 个有组ID的成就，%s 个不同的组", group_count, len(groups))
        for group_id, group_info in groups.items():
            logger.debug("组 %s: 名称=%s, 成员数=%s", group_id, group_info['name'], len(group_info['members']))
        
        # 按组ID排序（按数字顺序）
        sorted_groups = sorted(groups.items(), key=lambda x: int(x[0].split('_')[1]) if x[0].startswith('group_') else 0)
        
        # 填充表格
        self.groups_table.setRowCount(len(sorted_groups))
        selected_row = -1
        for i, (group_id, group_info) in enumerate(sorted_groups):
            # 组名称（只读）
            name_item = QTableWidgetItem(group_info['name'])
            name_item.setFlags(name_item.flags() & ~Qt.ItemFlag.ItemIsEditable)  # 移除可编辑标志
            # 存储组ID作为用户数据，用于后续操作
            name_item.setData(Qt.ItemDataRole.UserRole, group_id)
            logger.debug("设置表格项: 行=%s, 组ID=%s, 组名称=%s", i, group_id, group_info['name'])
            self.groups_table.setItem(i, 0, name_item)
            
            # 如果这个组之前被选中，记录新的行号
            if group_id == selected_group_id:
                selected_row = i
        
        # 恢复选中状态
        if selected_row >= 0:
            self.groups_table.setCurrentCell(selected_row, 0)
        elif len(groups) > 0:
            # 如果没有之前选中的组，但有组存在，选中第一行
            self.groups_table.setCurrentCell(0, 0)
        
        # 手动触发一次选择变化事件，确保成员列表正确更新
        if self.groups_table.rowCount() > 0:
            # 使用延迟调用，确保表格状态完全更新后再触发选择事件
            from PySide6.QtCore import QTimer
            QTimer.singleShot(50, lambda: self._on_group_cell_clicked(0, 0) if self.groups_table.rowCount() > 0 else None)
    
    def _generate_group_name(self, group_id):
        """根据组ID生成友好的组名"""
        if group_id.startswith('group_'):
            try:
                # 提取数字部分
                number = int(group_id.split('_')[1])
                return f"成就组 {number}"
            except (IndexError, ValueError):
                pass
        # 如果不是标准格式，返回原ID
        return group_id
    
    
    
    def _on_group_cell_clicked(self, row, column):
        """当点击成就组时更新右侧成员列表"""
        # 确保表格存在
        if not hasattr(self, 'groups_table') or self.groups_table.rowCount() == 0:
            self.current_group_label.setText("未选择")
            self.group_members_table.setRowCount(0)
            logger.debug("成就组表格为空")
            return
        
        current_row = row
        logger.debug("点击行: %s, 总行数: %s", current_row, self.groups_table.rowCount())
        
        if current_row < 0:
            # 如果没有点击任何行，清空显示
                self.current_group_label.setText("未选择")
                self.group_members_table.setRowCount(0)
                logger.debug("没有数据，清空显示")
                return
        
        name_item = self.groups_table.item(current_row, 0)
        if not name_item:
            self.current_group_label.setText("未选择")
            self.group_members_table.setRowCount(0)
            logger.debug("无法获取组名称项")
            return
        
        group_id = name_item.data(Qt.ItemDataRole.UserRole)
        if not group_id:
            self.current_group_label.setText("未选择")
            self.group_members_table.setRowCount(0)
            logger.debug("无法获取组ID")
            return
        
        # 显示组名称而不是组ID
        group_name = name_item.text()
        self.current_group_label.setText(group_name)
        logger.debug("选择的组: ID=%s, 名称=%s", group_id, group_name)
        logger.debug("选择的组ID: %s", group_id)

        # 加载该组的所有成员
        try:
            achievements = config.load_base_achievements()
            members = []
            for achievement in achievements:
                if achievement.get('成就组ID') == group_id:
                    members.append(achievement)

            logger.debug("组 %s 有 %s 个成员", group_id, len(members))
            
            # 填充成员表格
            self.group_members_table.setRowCount(len(members))
            for i, member in enumerate(members):
                # 名称 - 使用原始名称（不带组标识）
                display_name = member.get('原始名称', '') or member.get('名称', '')
                name_item = QTableWidgetItem(display_name)
                name_item.setFlags(name_item.flags() & ~Qt.ItemFlag.ItemIsEditable)  # 不可编辑
                name_item.setData(Qt.ItemDataRole.UserRole, member.get('编号', ''))  # 存储编号用于后续操作
                self.group_members_table.setItem(i, 0, name_item)
                
                # 描述
                desc = member.get('描述', '')
                if len(desc) > 100:  # 增加描述显示长度
                    desc = desc[:100] + "..."
                desc_item = QTableWidgetItem(desc)
                desc_item.setFlags(desc_item.flags() & ~Qt.ItemFlag.ItemIsEditable)  # 不可编辑
                self.group_members_table.setItem(i, 1, desc_item)
                
                # 移除 - 显示为可点击的文本
                remove_item = QTableWidgetItem("移除")
                remove_item.setFlags(remove_item.flags() & ~Qt.ItemFlag.ItemIsEditable)  # 不可编辑
                remove_item.setForeground(QColor(255, 69, 0))  # 红橙色文字
                remove_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)  # 居中显示
                self.group_members_table.setItem(i, 2, remove_item)
        except Exception as e:
            logger.error("加载组成员失败: %s", e)
            self.current_group_label.setText("加载失败")
            self.group_members_table.setRowCount(0)
    
    def _add_achievement_group(self):
        """添加成就组"""
        
        # 生成组ID和名称
        existing_groups = set()
        for row in range(self.groups_table.rowCount()):
            name_item = self.groups_table.item(row, 0)
            if name_item:
                group_id = name_item.data(Qt.ItemDataRole.UserRole)
                if group_id and group_id.startswith('group_'):
                    try:
                        num = int(group_id.split('_')[1])
                        existing_groups.add(num)
                        logger.debug("找到现有组: ID=%s, 编号=%s", group_id, num)
                    except:
                        pass
        
        # 找到下一个可用的编号
        next_num = 1
        while next_num in existing_groups:
            next_num += 1
        
        group_id = f"group_{next_num:03d}"
        group_name = f"成就组 {next_num}"
        
        logger.debug("创建新组: ID=%s, 名称=%s, 现有组编号: %s", group_id, group_name, sorted(existing_groups))
        
        # 添加新行
        row = self.groups_table.rowCount()
        self.groups_table.insertRow(row)
        
        # 组名称（可编辑）
        name_item = QTableWidgetItem(group_name)
        name_item.setFlags(name_item.flags() | Qt.ItemFlag.ItemIsEditable)
        # 存储组ID作为用户数据，用于后续操作
        name_item.setData(Qt.ItemDataRole.UserRole, group_id)
        self.groups_table.setItem(row, 0, name_item)
        
        # 选中新行
        self.groups_table.selectRow(row)
    
    def _delete_achievement_group(self):
        """删除成就组"""
        from core.custom_message_box import CustomMessageBox
        
        current_row = self.groups_table.currentRow()
        if current_row < 0:
            CustomMessageBox.warning(self, "警告", "请先选择要删除的成就组")
            return
        
        name_item = self.groups_table.item(current_row, 0)
        if not name_item:
            return
        
        group_id = name_item.data(Qt.ItemDataRole.UserRole)
        if not group_id:
            return
        
        # 确认删除
        reply = CustomMessageBox.question(self, "确认", f"确定要删除成就组 '{group_id}' 吗？\n这将清除所有相关成就的组信息。")
        if reply != QMessageBox.StandardButton.Yes:
            return
        
        # 从表格中删除
        self.groups_table.removeRow(current_row)
        
        # 清除成就数据中的组信息
        achievements = config.load_base_achievements()
        modified = False
        for achievement in achievements:
            if achievement.get('成就组ID') == group_id:
                achievement.pop('成就组ID', None)
                achievement.pop('互斥成就', None)
                modified = True
        
        if modified:
            config.save_base_achievements(achievements)
            # 重新加载成就组表格
            self._load_achievement_groups()
            CustomMessageBox.information(self, "成功", f"成就组 '{group_id}' 已删除")
        
        # 清空成员列表
        self.group_members_table.setRowCount(0)
        self.current_group_label.setText("未选择")
    
    def _on_member_cell_clicked(self, row, column):
        """处理成员表格单元格点击事件"""
        logger.debug("_on_member_cell_clicked 被调用: 行=%s, 列=%s", row, column)
        if column == 2:  # 只处理移除列的点击
            code_item = self.group_members_table.item(row, 0)
            if code_item:
                code = code_item.data(Qt.ItemDataRole.UserRole)  # 从UserRole获取编号
                # 获取当前选中行的实际group_id
                current_row = self.groups_table.currentRow()
                if current_row >= 0:
                    name_item = self.groups_table.item(current_row, 0)
                    if name_item:
                        group_id = name_item.data(Qt.ItemDataRole.UserRole)
                        logger.debug("准备移除成就: %s, 组ID: %s", code, group_id)
                        if group_id:
                            self._remove_achievement_from_group(group_id, code)
    
    def _fix_group_mutex_relations(self, group_id, achievements):
        """修复组内成就的互斥关系"""
        # 获取组内所有成员
        group_members = []
        for achievement in achievements:
            if achievement.get('成就组ID') == group_id:
                group_members.append(achievement['编号'])
        
        # 为每个成员设置互斥列表
        for achievement in achievements:
            if achievement.get('成就组ID') == group_id:
                code = achievement['编号']
                # 互斥列表是组内其他所有成员
                mutex_list = [m for m in group_members if m != code]
                achievement['互斥成就'] = mutex_list
    
    def _load_group_members(self, group_id):
        """只加载指定组的成员"""
        try:
            achievements = config.load_base_achievements()
            members = []
            for achievement in achievements:
                if achievement.get('成就组ID') == group_id:
                    members.append(achievement)

            logger.debug("组 %s 有 %s 个成员", group_id, len(members))

            # 填充成员表格
            self.group_members_table.setRowCount(len(members))
            for i, member in enumerate(members):
                # 名称 - 使用原始名称（不带组标识）
                display_name = member.get('原始名称', '') or member.get('名称', '')
                name_item = QTableWidgetItem(display_name)
                name_item.setFlags(name_item.flags() & ~Qt.ItemFlag.ItemIsEditable)  # 不可编辑
                name_item.setData(Qt.ItemDataRole.UserRole, member.get('编号', ''))  # 存储编号用于后续操作
                self.group_members_table.setItem(i, 0, name_item)
                
                # 描述
                desc = member.get('描述', '')
                if len(desc) > 100:  # 增加描述显示长度
                    desc = desc[:100] + "..."
                desc_item = QTableWidgetItem(desc)
                desc_item.setFlags(desc_item.flags() & ~Qt.ItemFlag.ItemIsEditable)  # 不可编辑
                self.group_members_table.setItem(i, 1, desc_item)
                
                # 移除 - 显示为可点击的文本
                remove_item = QTableWidgetItem("移除")
                remove_item.setFlags(remove_item.flags() & ~Qt.ItemFlag.ItemIsEditable)  # 不可编辑
                remove_item.setForeground(QColor(255, 69, 0))  # 红橙色文字
                remove_item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)  # 居中显示
                self.group_members_table.setItem(i, 2, remove_item)
        except Exception as e:
            logger.error("加载组成员失败: %s", e)

    def _add_group_member(self):
        """添加组内成就"""
        current_row = self.groups_table.currentRow()
        if current_row < 0:
            from core.custom_message_box import CustomMessageBox
            CustomMessageBox.warning(self, "警告", "请先选择一个成就组")
            return
        
        name_item = self.groups_table.item(current_row, 0)
        if not name_item:
            return
        
        group_id = name_item.data(Qt.ItemDataRole.UserRole)
        if not group_id:
            return
        
        # 显示成就选择对话框
        dialog = AchievementSelectionDialog(self)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            selected_achievements = dialog.get_selected_achievements()
            if selected_achievements:
                self._add_achievements_to_group(group_id, selected_achievements)
    
    def _add_achievements_to_group(self, group_id, achievements):
        """将成就添加到组中"""
        
        logger.debug("开始添加 %s 个成就到组 %s", len(achievements), group_id)
        
        # 加载当前成就数据
        all_achievements = config.load_base_achievements()
        logger.debug("加载了 %s 个成就", len(all_achievements))
        
        # 获取当前组的所有成员
        current_members = []
        for achievement in all_achievements:
            if achievement.get('成就组ID') == group_id:
                current_members.append(achievement['编号'])
        logger.debug("组 %s 当前有 %s 个成员: %s", group_id, len(current_members), current_members)
        
        # 显示要添加的成就
        for achievement in achievements:
            logger.debug("要添加的成就: %s - %s", achievement.get('编号', ''), achievement.get('名称', ''))
        
        # 添加新成员
        modified = False
        added_count = 0
        for achievement in achievements:
            code = achievement['编号']
            logger.debug("处理成就 %s", code)
            if code not in current_members:
                # 为每个成就添加组信息
                for full_achievement in all_achievements:
                    if full_achievement['编号'] == code:
                        logger.debug("找到成就 %s，设置组ID为 %s", code, group_id)
                        full_achievement['成就组ID'] = group_id
                        # 互斥成就列表将在修复时自动生成
                        modified = True
                        added_count += 1
                        break
                else:
                    logger.error("未找到编号为 %s 的成就", code)
            else:
                logger.debug("成就 %s 已在组中", code)
        
        logger.debug("添加了 %s 个新成就，修改状态: %s", added_count, modified)

        if modified:
            logger.debug("开始修复组 %s 的互斥关系", group_id)
            # 修复互斥关系
            self._fix_group_mutex_relations(group_id, all_achievements)
            # 保存数据
            logger.debug("保存成就数据")
            config.save_base_achievements(all_achievements)
            # 刷新显示
            logger.debug("重新加载成就组表格")
            self._load_achievement_groups()  # 重新加载成就组表格（会自动刷新成员列表）
        else:
            logger.debug("没有成就被添加到组 %s", group_id)
    

    
    
    
    def _remove_member_at_row(self, row):
        """移除指定行的成员"""
        code_item = self.group_members_table.item(row, 0)
        if not code_item:
            return
        
        code = code_item.text()
        group_id = self.current_group_label.text()
        
        self._remove_achievement_from_group(group_id, code)
    
    def _remove_achievement_from_group(self, group_id, code):
        """从组中移除成就"""
        from core.custom_message_box import CustomMessageBox
        
        logger.debug("开始移除成就: %s 从组 %s", code, group_id)

        # 确认删除
        group_name = self.current_group_label.text()
        logger.debug("显示确认对话框，组名: %s", group_name)
        reply = CustomMessageBox.question(self, "确认", f"确定要将成就 '{code}' 从组 '{group_name}' 中移除吗？")
        logger.debug("确认对话框返回值: %s, CustomMessageBox.Yes=%s", reply, CustomMessageBox.Yes)
        if reply != CustomMessageBox.Yes:
            logger.debug("用户取消移除成就")
            return
        logger.debug("用户确认移除成就")

        # 加载成就数据
        logger.debug("开始加载成就数据")
        achievements = config.load_base_achievements()
        logger.debug("加载了 %s 个成就", len(achievements))
        modified = False

        # 移除组信息
        logger.debug("查找成就: code='%s', group_id='%s'", code, group_id)
        for i, achievement in enumerate(achievements):
            if i < 5:  # 只打印前5个，避免日志过长
                logger.debug("成就%s: 编号='%s', 组ID='%s'",
                             i, achievement.get('编号', ''), achievement.get('成就组ID', ''))
            if achievement['编号'] == code and achievement.get('成就组ID') == group_id:
                logger.debug("找到成就 %s，正在移除组信息", code)
                achievement.pop('成就组ID', None)
                achievement.pop('互斥成就', None)
                modified = True
                break

        if not modified:
            logger.debug("警告：未找到要移除的成就 %s 在组 %s 中", code, group_id)
            return

        logger.debug("成就信息已修改，开始处理组内剩余成员")
        # 检查组内剩余成员数量，如果只有1个成员则解散该组
        remaining_members = [a for a in achievements if a.get('成就组ID') == group_id]
        logger.debug("组 %s 剩余成员数量: %s", group_id, len(remaining_members))
        if len(remaining_members) <= 1:
            # 解散该组，清除剩余成员的组信息
            logger.debug("解散组 %s", group_id)
            for achievement in remaining_members:
                achievement.pop('成就组ID', None)
                achievement.pop('互斥成就', None)
        else:
            # 修复组内剩余成员的互斥关系
            logger.debug("修复组 %s 的互斥关系", group_id)
            self._fix_group_mutex_relations(group_id, achievements)

        # 保存数据
        logger.debug("开始保存成就数据")
        config.save_base_achievements(achievements)
        logger.debug("成就数据已保存")
        # 刷新显示 - 只刷新当前组，避免触发完整重新加载
        logger.debug("开始刷新当前组显示")
        current_row = self.groups_table.currentRow()
        if current_row >= 0:
            # 重新加载当前组的成员
            name_item = self.groups_table.item(current_row, 0)
            if name_item:
                group_id = name_item.data(Qt.ItemDataRole.UserRole)
                self._load_group_members(group_id)
        logger.debug("当前组显示已刷新")
    
    

    
    def _on_tab_changed(self, index):
        """处理tab切换事件"""
        # 获取当前tab的标题
        tab_text = self.tab_widget.tabText(index)
        
        # 如果切换到成就组管理tab，重新加载数据
        if "成就组管理" in tab_text:
            if hasattr(self, 'groups_table'):
                self._load_achievement_groups()
                # _load_achievement_groups已经处理了选中状态，会自动调用_on_group_selection_changed
    
    def _add_first_category_row(self):
        """在第一分类表格中添加空白行"""
        row = self.first_category_table.rowCount()
        self.first_category_table.insertRow(row)
        
        # 添加可编辑的项目
        name_item = QTableWidgetItem("")
        name_item.setFlags(name_item.flags() | Qt.ItemFlag.ItemIsEditable)
        self.first_category_table.setItem(row, 0, name_item)
        
        # 自动选中新行的第一个单元格
        self.first_category_table.setCurrentCell(row, 0)
        self.first_category_table.editItem(name_item)
    
    def _delete_first_category(self):
        """删除第一分类"""
        from core.custom_message_box import CustomMessageBox
        
        current_row = self.first_category_table.currentRow()
        if current_row >= 0:
            name = self.first_category_table.item(current_row, 0).text()
            reply = CustomMessageBox.question(self, "确认删除", f"确定要删除分类 '{name}' 吗？")
            if reply == CustomMessageBox.Yes:
                self.first_category_table.removeRow(current_row)
                
                # 从下拉框中移除
                index = self.first_category_combo.findText(name)
                if index >= 0:
                    self.first_category_combo.removeItem(index)
    
    def _add_second_category_row(self):
        """在第二分类表格中添加空白行"""
        row = self.second_category_table.rowCount()
        self.second_category_table.insertRow(row)
        
        # 添加可编辑的项目
        name_item = QTableWidgetItem("")
        name_item.setFlags(name_item.flags() | Qt.ItemFlag.ItemIsEditable)
        self.second_category_table.setItem(row, 0, name_item)
        
        # 自动选中新行的第一个单元格
        self.second_category_table.setCurrentCell(row, 0)
        self.second_category_table.editItem(name_item)
    
    def _delete_second_category(self):
        """删除第二分类"""
        from core.custom_message_box import CustomMessageBox
        
        current_row = self.second_category_table.currentRow()
        if current_row >= 0:
            name = self.second_category_table.item(current_row, 0).text()
            reply = CustomMessageBox.question(self, "确认删除", f"确定要删除分类 '{name}' 吗？")
            if reply == CustomMessageBox.Yes:
                self.second_category_table.removeRow(current_row)
    
    def _load_second_categories(self):
        """加载指定第一分类的第二分类"""
        from core.config import config
        
        # 先保存当前正在编辑的第二分类数据到缓存
        if hasattr(self, '_last_first_category') and self._last_first_category:
            self._save_current_second_categories_to_cache(self._last_first_category)
        
        # 加载新的第二分类数据
        first_category = self.first_category_combo.currentText()
        self._last_first_category = first_category
        
        # 优先从缓存加载，如果缓存中没有则从配置文件加载
        if not hasattr(self, '_second_categories_cache'):
            self._second_categories_cache = {}
        
        if first_category in self._second_categories_cache:
            second_categories = self._second_categories_cache[first_category]
        else:
            category_config = config.load_category_config()
            second_categories = category_config.get("second_categories", {}).get(first_category, {})
            self._second_categories_cache[first_category] = second_categories.copy()
        
        # 清空表格并加载数据
        # 按后缀值排序后显示
        sorted_categories = sorted(second_categories.items(), key=lambda x: int(x[1]))
        self.second_category_table.setRowCount(len(sorted_categories))
        for i, (name, suffix) in enumerate(sorted_categories):
            self.second_category_table.setItem(i, 0, QTableWidgetItem(name))
    
    def _save_current_second_categories_to_cache(self, first_category):
        """保存当前第二分类数据到缓存"""
        if not hasattr(self, '_second_categories_cache'):
            self._second_categories_cache = {}
        
        second_cats = {}
        for row in range(self.second_category_table.rowCount()):
            name_item = self.second_category_table.item(row, 0)
            
            if name_item:
                name = name_item.text().strip()
                
                if name:
                    # 根据表格行顺序分配后缀值（从10开始，每次递增10）
                    suffix = str((row + 1) * 10)
                    second_cats[name] = suffix
        
        self._second_categories_cache[first_category] = second_cats
    
    def _save_category_config_silent(self):
        """静默保存分类配置（不显示提示框）"""
        from core.config import config
        
        # 先保存当前正在编辑的第二分类数据到缓存
        current_first = self.first_category_combo.currentText()
        self._save_current_second_categories_to_cache(current_first)
        
        # 直接使用缓存中的数据
        all_second_categories = {}
        if hasattr(self, '_second_categories_cache'):
            all_second_categories = self._second_categories_cache.copy()
        else:
            # 如果缓存不存在，从配置文件加载
            category_config = config.load_category_config()
            all_second_categories = category_config.get("second_categories", {})
        
        # 收集第一分类数据，根据表格行顺序重新分配排序值
        first_categories = {}
        for row in range(self.first_category_table.rowCount()):
            name_item = self.first_category_table.item(row, 0)
            
            if name_item:
                name = name_item.text().strip()
                
                if name:  # 只保存非空行
                    # 根据表格行顺序分配排序值（从1开始）
                    first_categories[name] = row + 1
        
        # 保存配置
        updated_config = {
            "first_categories": first_categories,
            "second_categories": all_second_categories
        }
        
        config.save_category_config(updated_config)
        
        # 重新编码所有用户的存档数据
        config.reencode_all_user_progress()
        
        # 发送分类配置更新信号
        signal_bus.category_config_updated.emit()
        
        # 刷新表格显示
        self._refresh_category_tables()
    
    def _save_category_config(self):
        """保存分类配置（显示提示框）"""
        from core.config import config
        from core.custom_message_box import CustomMessageBox
        
        # 收集第一分类数据，根据表格行顺序重新分配排序值
        first_categories = {}
        for row in range(self.first_category_table.rowCount()):
            name_item = self.first_category_table.item(row, 0)
            
            if name_item:
                name = name_item.text().strip()
                
                if name:  # 只保存非空行
                    # 根据表格行顺序分配排序值（从1开始）
                    first_categories[name] = row + 1
        
        # 先保存当前正在编辑的第二分类数据到缓存
        current_first = self.first_category_combo.currentText()
        self._save_current_second_categories_to_cache(current_first)
        
        # 使用缓存中的所有第二分类数据
        all_second_categories = {}
        if hasattr(self, '_second_categories_cache'):
            all_second_categories = self._second_categories_cache.copy()
        else:
            # 如果缓存不存在，从配置文件加载
            category_config = config.load_category_config()
            all_second_categories = category_config.get("second_categories", {})
        
        # 保存配置
        updated_config = {
            "first_categories": first_categories,
            "second_categories": all_second_categories
        }
        
        if config.save_category_config(updated_config):
            # 重新编码所有用户的存档数据
            config.reencode_all_user_progress()
            
            # 发送分类配置更新信号
            signal_bus.category_config_updated.emit()
            
            # 刷新表格显示
            self._refresh_category_tables()
            
            CustomMessageBox.information(self, "成功", "分类配置已保存，所有用户存档数据已自动更新")
            # 更新下拉框
            self._refresh_first_category_combo()
        else:
            CustomMessageBox.warning(self, "错误", "保存分类配置失败")
    
    def _refresh_first_category_combo(self):
        """刷新第一分类下拉框"""
        current_text = self.first_category_combo.currentText()
        self.first_category_combo.clear()
        
        for row in range(self.first_category_table.rowCount()):
            name_item = self.first_category_table.item(row, 0)
            if name_item:
                name = name_item.text().strip()
                if name:
                    self.first_category_combo.addItem(name)
        
        # 恢复之前的选择
        index = self.first_category_combo.findText(current_text)
        if index >= 0:
            self.first_category_combo.setCurrentIndex(index)
    
    

    def _load_background_image(self):
        """加载背景图片"""
        self.background_pixmap = load_background_image(config.theme)
    
    def _refresh_category_tables(self):
        """刷新分类表格显示"""
        # 重新加载分类配置
        category_config = config.load_category_config()
        
        # 刷新第一分类表格
        first_categories = category_config.get("first_categories", {})
        sorted_categories = sorted(first_categories.items(), key=lambda x: x[1])
        self.first_category_table.setRowCount(len(sorted_categories))
        for i, (name, order) in enumerate(sorted_categories):
            self.first_category_table.setItem(i, 0, QTableWidgetItem(name))
        
        # 刷新第二分类表格
        current_first = self.first_category_combo.currentText()
        if current_first:
            second_categories = category_config.get("second_categories", {}).get(current_first, {})
            sorted_second = sorted(second_categories.items(), key=lambda x: int(x[1]))
            self.second_category_table.setRowCount(len(sorted_second))
            for i, (name, suffix) in enumerate(sorted_second):
                self.second_category_table.setItem(i, 0, QTableWidgetItem(name))
    
    def _on_first_category_row_moved(self, source_row, target_row):
        """处理第一分类表格行移动事件"""
        # 拖动后分类名称位置改变，但排序值保持不变
        pass
        
    def _on_second_category_row_moved(self, source_row, target_row):
        """处理第二分类表格行移动事件"""
        # 拖动后分类名称位置改变，但后缀值保持不变
        pass
    
    def _reencode_achievements(self):
        """重新编号所有成就数据"""
        # 确认对话框
        reply = CustomMessageBox.question(
            self,
            "确认重新编号",
            "重新编号将根据当前分类配置重新生成所有成就的编号和绝对编号。\n\n"
            "此操作会修改基础成就数据和所有用户的进度数据。\n"
            "是否继续？",
            ("是", "否")
        )
        
        if reply != CustomMessageBox.Yes:
            return
        
        try:
            # 调用config的reencode_all_user_progress方法
            success = config.reencode_all_user_progress()
            
            if success:
                CustomMessageBox.information(
                    self,
                    "重新编号完成",
                    "所有成就数据已根据当前分类配置重新编号。\n"
                    "成就管理标签页的数据已自动刷新。"
                )
                
                # 通知主窗口刷新数据
                signal_bus.category_config_updated.emit()
            else:
                CustomMessageBox.warning(
                    self,
                    "重新编号失败",
                    "重新编号过程中出现错误，请检查日志获取详细信息。"
                )
                
        except Exception as e:
            CustomMessageBox.critical(
                self,
                "错误",
                f"重新编号时发生错误：\n{str(e)}"
            )
            import traceback
            traceback.print_exc()

    def refresh_version_list(self):
        """刷新版本列表"""
        # 获取当前成就数据
        from core.main_window import TemplateMainWindow
        from PySide6.QtWidgets import QApplication
        main_window = None
        for widget in QApplication.topLevelWidgets():
            if isinstance(widget, TemplateMainWindow):
                main_window = widget
                break
        
        if not main_window or not hasattr(main_window, 'manage_tab'):
            return
        
        achievements = main_window.manage_tab.manager.achievements
        
        # 收集所有版本
        versions = set()
        for achievement in achievements:
            version = achievement.get('版本', '')
            if version:
                versions.add(version)
        
        # 清空并重新填充下拉框
        self.version_combo.clear()
        if versions:
            sorted_versions = sorted(versions, key=lambda x: float(x) if x.replace('.', '').isdigit() else 0)
            self.version_combo.addItems(sorted_versions)
        
        # 如果有版本，启用删除按钮
        self.delete_version_btn.setEnabled(bool(versions))

    def delete_version_data(self):
        """删除指定版本的成就数据"""
        selected_version = self.version_combo.currentText()
        if not selected_version:
            CustomMessageBox.warning(self, "警告", "请先选择要删除的版本！")
            return
        
        # 确认删除
        reply = CustomMessageBox.question(
            self, 
            "确认删除", 
            f"确定要删除版本 {selected_version} 的所有成就数据吗？\n\n"
            f"删除前会自动导出所有用户进度数据作为备份。\n"
            f"此操作不可恢复！",
            ("确定", "取消")
        )
        
        if reply != CustomMessageBox.Yes:
            return
        
        try:
            # 获取主窗口和管理标签页
            from core.main_window import TemplateMainWindow
            from PySide6.QtWidgets import QApplication
            main_window = None
            for widget in QApplication.topLevelWidgets():
                if isinstance(widget, TemplateMainWindow):
                    main_window = widget
                    break
            
            if not main_window or not hasattr(main_window, 'manage_tab'):
                raise Exception("无法访问成就管理数据")
            
            manage_tab = main_window.manage_tab
            all_achievements = manage_tab.manager.achievements
            
            # 1. 找出要删除的版本数据
            to_delete_achievements = [ach for ach in all_achievements if ach.get('版本', '') == selected_version]
            
            if not to_delete_achievements:
                CustomMessageBox.information(self, "提示", f"没有找到版本 {selected_version} 的成就数据")
                return
            
            # 2. 导出要删除的版本数据和所有用户进度
            from datetime import datetime
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            
            # 获取exe同目录路径
            from core.config import get_resource_path
            backup_dir = get_resource_path("")
            
            # 导出所有用户进度到一个Excel文件
            users_backup_filename = f"版本{selected_version}_所有用户进度_{timestamp}.xlsx"
            users_backup_path = backup_dir / users_backup_filename
            self._export_all_users_progress_to_excel(to_delete_achievements, str(users_backup_path))
            user_backup_files = [users_backup_filename]
            
            # 3. 删除指定版本的成就
            filtered_achievements = [ach for ach in all_achievements if ach.get('版本', '') != selected_version]
            deleted_count = len(to_delete_achievements)
            
            # 4. 更新数据（不需要重新编码）
            manage_tab.manager.achievements = filtered_achievements
            manage_tab.manager.filtered_achievements = filtered_achievements.copy()
            
            # 5. 更新界面
            manage_tab.manager_table.load_data(filtered_achievements)
            manage_tab.update_filters()
            manage_tab.update_statistics()
            manage_tab.save_to_json()
            
            # 6. 刷新版本列表
            self.refresh_version_list()
            
            # 7. 显示成功消息
            success_msg = f"成功删除版本 {selected_version} 的 {deleted_count} 条成就数据！\n\n"
            success_msg += f"备份文件：\n"
            if user_backup_files:
                success_msg += f"• 用户进度（{len(user_backup_files)}个文件）：\n"
                for file in user_backup_files:
                    success_msg += f"  - {file}\n"
                success_msg += f"\n注意：备份文件包含所有用户的工作表，工作表名为UID"
            CustomMessageBox.information(self, "删除成功", success_msg)
            
        except Exception as e:
            error_msg = f"删除版本数据失败：{str(e)}"
            CustomMessageBox.critical(self, "错误", error_msg)
            logger.error("%s", error_msg)

    def _export_all_users_progress_to_excel(self, achievements, file_path):
            """导出所有用户的进度数据到一个Excel文件的多个工作表"""
            from openpyxl import Workbook
            from openpyxl.styles import Font, Alignment, PatternFill, Border, Side
            from openpyxl.utils import get_column_letter

            wb = Workbook()
            # 删除默认工作表
            wb.remove(wb.active)

            # 获取所有用户
            users = config.get_users()

            # 定义列顺序
            column_order = [
                '名称', '描述', '奖励', '版本', '是否隐藏', '第一分类', '第二分类', '获取状态'
            ]

            # 为每个用户创建工作表
            for username in users.keys():
                # 获取用户UID作为工作表名称
                user_info = users.get(username, {})
                uid = user_info.get('uid', username) if isinstance(user_info, dict) else username
                sheet_name = str(uid)[:31]  # Excel工作表名称最大31个字符

                # 创建工作表
                sheet = wb.create_sheet(title=sheet_name)

                # 写入表头
                for col_idx, field_name in enumerate(column_order, 1):
                    cell = sheet.cell(row=1, column=col_idx, value=field_name)
                    cell.fill = PatternFill(start_color="4472C4", end_color="4472C4", fill_type="solid")
                    cell.font = Font(color="FFFFFF", bold=True)
                    cell.alignment = Alignment(horizontal="center")

                # 加载用户进度数据
                user_progress = config.load_user_progress(username)
                if not user_progress:
                    logger.info("用户 %s (UID: %s) 没有进度数据", username, uid)
                    user_progress = {}

                # 写入数据
                row = 2
                for achievement in achievements:
                    # 获取成就编号
                    code = achievement.get('编号', '')

                    # 创建行数据
                    row_data = []
                    for field_name in column_order[:-1]:  # 除了获取状态
                        value = achievement.get(field_name, '')
                        row_data.append(value)

                    # 获取用户进度
                    progress_info = user_progress.get(code, None)
                    if progress_info and isinstance(progress_info, dict):
                        status = progress_info.get('获取状态', '未完成')
                    else:
                        status = '未完成'

                    row_data.append(status)

                    # 写入行
                    for col_idx, value in enumerate(row_data, 1):
                        cell = sheet.cell(row=row, column=col_idx, value=value)

                        # 设置边框
                        thin_border = Border(
                            left=Side(style='thin'),
                            right=Side(style='thin'),
                            top=Side(style='thin'),
                            bottom=Side(style='thin')
                        )
                        cell.border = thin_border

                        # 特殊处理名称列
                        if col_idx == 1 and value:  # 第一列是名称
                            cell.font = Font(bold=True)

                            # 隐藏成就用橙色
                            if achievement.get('是否隐藏') == '隐藏':
                                cell.font = Font(bold=True, color="FFA500")

                    row += 1

                # 调整列宽
                column_widths = {
                    '名称': 25, '描述': 40, '版本': 10, '奖励': 15, '是否隐藏': 10,
                    '第一分类': 15, '第二分类': 20, '获取状态': 10
                }

                for col_idx, field_name in enumerate(column_order, 1):
                    col_letter = get_column_letter(col_idx)
                    sheet.column_dimensions[col_letter].width = column_widths.get(field_name, 15)

            # 保存文件
            wb.save(file_path)
            logger.info("所有用户的进度数据已导出到: %s", file_path)

    def _export_achievements_to_excel(self, achievements, file_path, include_status=True):
        """导出成就数据到Excel文件"""
        from openpyxl import Workbook
        from openpyxl.styles import Font, Alignment, PatternFill, Border, Side
        from openpyxl.utils import get_column_letter
        
        wb = Workbook()
        sheet = wb.active
        sheet.title = "成就数据"
        
        # 定义列顺序（与数据爬取导出保持一致）
        column_order = [
            '名称', '描述', '奖励', '版本', '是否隐藏', '第一分类', '第二分类'
        ]
        if include_status:
            column_order.append('获取状态')
        
        # 写入表头
        for col_idx, field_name in enumerate(column_order, 1):
            cell = sheet.cell(row=1, column=col_idx, value=field_name)
            cell.fill = PatternFill(start_color="4472C4", end_color="4472C4", fill_type="solid")
            cell.font = Font(color="FFFFFF", bold=True)
            cell.alignment = Alignment(horizontal="center")
        
        # 写入数据
        for row_idx, achievement in enumerate(achievements, start=2):
            for col_idx, field_name in enumerate(column_order, 1):
                value = achievement.get(field_name, '')
                
                # 特殊处理名称列
                if field_name == '名称' and value:
                    cell = sheet.cell(row=row_idx, column=col_idx, value=value)
                    cell.font = Font(bold=True)
                    
                    # 隐藏成就用橙色
                    if achievement.get('是否隐藏') == '隐藏':
                        cell.font = Font(bold=True, color="FFA500")
                else:
                    cell = sheet.cell(row=row_idx, column=col_idx, value=str(value))
                
                # 设置边框
                thin_border = Border(
                    left=Side(style='thin'),
                    right=Side(style='thin'),
                    top=Side(style='thin'),
                    bottom=Side(style='thin')
                )
                cell.border = thin_border
        
        # 调整列宽
        column_widths = {
            '名称': 25, '描述': 40, '奖励': 15, '版本': 10, '是否隐藏': 10, 
            '第一分类': 15, '第二分类': 20, '获取状态': 10
        }
        
        for col_idx, field_name in enumerate(column_order, 1):
            col_letter = get_column_letter(col_idx)
            sheet.column_dimensions[col_letter].width = column_widths.get(field_name, 15)
        
        # 保存文件
        wb.save(file_path)
        logger.info("版本数据已导出到: %s", file_path)


class AchievementSelectionDialog(QDialog):
    """成就选择对话框"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("选择成就")
        self.setModal(True)
        self.setFixedSize(800, 600)
        
        # 设置无边框窗口
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.Dialog)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        
        from core.config import config
        self.setStyleSheet(get_dialog_style(config.theme))
        
        # 加载背景图片
        from core.widgets import load_background_image, BackgroundWidget
        self.background_pixmap = load_background_image(config.theme)
        
        # 创建带背景的中央部件
        central_widget = BackgroundWidget(self.background_pixmap, config.theme)
        layout = QVBoxLayout(central_widget)
        layout.setContentsMargins(0, 0, 0, 0)
        
        # 添加自定义标题栏
        from core.custom_title_bar import CustomTitleBar
        self.title_bar = CustomTitleBar(self)
        layout.addWidget(self.title_bar)
        
        # 内容区域
        content_widget = QWidget()
        content_layout = QVBoxLayout(content_widget)
        content_layout.setContentsMargins(20, 20, 20, 20)
        content_layout.setSpacing(15)
        layout.addWidget(content_widget)
        
        # 保存布局引用
        self.main_layout = content_layout
        
        self.selected_achievements = []
        self._init_ui()
        self._load_achievements()
        
        # 设置中央部件
        self.setLayout(QVBoxLayout())
        self.layout().addWidget(central_widget)
    
    def _init_ui(self):
        """初始化UI"""
        from core.styles import get_text_input_style, get_label_style
        
        # 搜索框
        search_layout = QHBoxLayout()
        search_label = QLabel("搜索：")
        search_label.setStyleSheet(get_label_style(config.theme))
        search_layout.addWidget(search_label)
        
        self.search_edit = QLineEdit()
        self.search_edit.setStyleSheet(get_text_input_style(config.theme))
        self.search_edit.textChanged.connect(self._filter_achievements)
        search_layout.addWidget(self.search_edit)
        self.main_layout.addLayout(search_layout)
        
        # 成就列表
        self.achievements_table = QTableWidget()
        self.achievements_table.setColumnCount(5)
        self.achievements_table.setHorizontalHeaderLabels(["", "编号", "名称", "描述", "分类"])
        self.achievements_table.horizontalHeader().setStretchLastSection(True)
        self.achievements_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.achievements_table.setAlternatingRowColors(True)
        
        # 去掉网格线（包括竖线）
        self.achievements_table.setShowGrid(False)
        
        # 去掉选中框和焦点
        self.achievements_table.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.achievements_table.setSelectionMode(QTableWidget.SelectionMode.NoSelection)  # 完全禁止选择
        
        # 应用滚动条样式
        from core.styles import get_scrollbar_style
        self.achievements_table.setStyleSheet(self.achievements_table.styleSheet() + get_scrollbar_style(config.theme))
        
        self.main_layout.addWidget(self.achievements_table)
        
        # 按钮区域
        button_layout = QHBoxLayout()
        button_layout.addStretch()
        
        from core.styles import get_button_style
        select_all_btn = QPushButton("全选")
        select_all_btn.setStyleSheet(get_button_style(config.theme))
        select_all_btn.clicked.connect(self._select_all)
        button_layout.addWidget(select_all_btn)
        
        clear_btn = QPushButton("清空")
        clear_btn.setStyleSheet(get_button_style(config.theme))
        clear_btn.clicked.connect(self._clear_selection)
        button_layout.addWidget(clear_btn)
        
        ok_btn = QPushButton("确定")
        ok_btn.setStyleSheet(get_button_style(config.theme))
        ok_btn.clicked.connect(lambda: self.done(QDialog.DialogCode.Accepted))
        button_layout.addWidget(ok_btn)
        
        cancel_btn = QPushButton("取消")
        cancel_btn.setStyleSheet(get_button_style(config.theme))
        cancel_btn.clicked.connect(lambda: self.done(QDialog.DialogCode.Rejected))
        button_layout.addWidget(cancel_btn)
        
        self.main_layout.addLayout(button_layout)
    
    def _load_achievements(self):
        """加载成就数据"""
        from core.config import config
        achievements = config.load_base_achievements()
        
        # 过滤掉已经有组的成就
        available_achievements = []
        for achievement in achievements:
            if not achievement.get('成就组ID'):
                available_achievements.append(achievement)
        
        self.all_achievements = available_achievements
        self._display_achievements(available_achievements)
    
    def _display_achievements(self, achievements):
        """显示成就列表"""
        from PySide6.QtWidgets import QCheckBox
        
        self.achievements_table.setRowCount(len(achievements))
        self.current_displayed_achievements = achievements  # 保存当前显示的成就列表
        
        for i, achievement in enumerate(achievements):
            # 复选框
            checkbox = QCheckBox()
            # 检查是否已选中
            if achievement in self.selected_achievements:
                checkbox.setChecked(True)
            checkbox.stateChanged.connect(lambda state, row=i: self._on_checkbox_changed(row, state))
            self.achievements_table.setCellWidget(i, 0, checkbox)
            
            # 编号
            code_item = QTableWidgetItem(achievement.get('编号', ''))
            code_item.setToolTip(achievement.get('编号', ''))
            self.achievements_table.setItem(i, 1, code_item)
            
            # 名称
            name_item = QTableWidgetItem(achievement.get('名称', ''))
            name_item.setToolTip(achievement.get('名称', ''))
            self.achievements_table.setItem(i, 2, name_item)
            
            # 描述
            full_desc = achievement.get('描述', '')
            desc = full_desc
            if len(desc) > 100:
                desc = desc[:100] + "..."
            desc_item = QTableWidgetItem(desc)
            desc_item.setToolTip(full_desc)  # 悬浮显示完整描述
            self.achievements_table.setItem(i, 3, desc_item)
            
            # 分类
            first_cat = achievement.get('第一分类', '')
            second_cat = achievement.get('第二分类', '')
            category = f"{first_cat} > {second_cat}" if first_cat and second_cat else first_cat or second_cat
            category_item = QTableWidgetItem(category)
            category_item.setToolTip(category)
            self.achievements_table.setItem(i, 4, category_item)
        
        # 设置列宽 - 调整列宽（保留用户要求的调整）
        self.achievements_table.setColumnWidth(0, 40)   # 复选框列 - 缩短
        self.achievements_table.setColumnWidth(1, 80)   # 编号列
        self.achievements_table.setColumnWidth(2, 130)  # 名称列
        self.achievements_table.setColumnWidth(3, 260)  # 描述列 - 加宽
        self.achievements_table.setColumnWidth(4, 130)  # 分类列 - 缩短三分之一
    
    def _filter_achievements(self):
        """过滤成就"""
        search_text = self.search_edit.text().lower()
        
        if not search_text:
            self._display_achievements(self.all_achievements)
            return
        
        filtered = []
        for achievement in self.all_achievements:
            if (search_text in achievement.get('名称', '').lower() or
                search_text in achievement.get('描述', '').lower() or
                search_text in achievement.get('编号', '').lower()):
                filtered.append(achievement)
        
        self._display_achievements(filtered)
    
    def _on_checkbox_changed(self, row, state):
        """复选框状态改变"""
        if hasattr(self, 'current_displayed_achievements'):
            achievement = self.current_displayed_achievements[row]
        else:
            achievement = self.all_achievements[row]
            
        if state == 2:  # 选中
            if achievement not in self.selected_achievements:
                self.selected_achievements.append(achievement)
        else:  # 取消选中
            if achievement in self.selected_achievements:
                self.selected_achievements.remove(achievement)
    
    def _select_all(self):
        """全选"""
        # 选择当前显示的所有成就
        if hasattr(self, 'current_displayed_achievements'):
            display_achievements = self.current_displayed_achievements
        else:
            display_achievements = self.all_achievements
            
        # 添加到已选择列表（避免重复）
        for achievement in display_achievements:
            if achievement not in self.selected_achievements:
                self.selected_achievements.append(achievement)
        
        # 更新所有复选框
        for i in range(self.achievements_table.rowCount()):
            checkbox = self.achievements_table.cellWidget(i, 0)
            if checkbox:
                checkbox.blockSignals(True)
                checkbox.setChecked(True)
                checkbox.blockSignals(False)
    
    def _clear_selection(self):
        """清空选择"""
        self.selected_achievements = []
        
        # 更新所有复选框
        for i in range(self.achievements_table.rowCount()):
            checkbox = self.achievements_table.cellWidget(i, 0)
            if checkbox:
                checkbox.blockSignals(True)
                checkbox.setChecked(False)
                checkbox.blockSignals(False)
    
    def get_selected_achievements(self):
        """获取选中的成就"""
        return self.selected_achievements
    
    
        
    
    
    