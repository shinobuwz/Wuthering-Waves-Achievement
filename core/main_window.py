import logging
from PySide6.QtWidgets import (
    QButtonGroup, QDialog, QFrame, QHBoxLayout, QLabel, QMainWindow,
    QPushButton, QStackedWidget, QVBoxLayout, QWidget,
)
from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QPainter, QPainterPath

logger = logging.getLogger(__name__)

from core.config import config
from core.signal_bus import signal_bus

from core.styles import get_main_window_style


class MainBackgroundWidget(QWidget):
    """Main shell that paints the configured image behind translucent UI panels."""

    def __init__(self, theme="light", parent=None):
        super().__init__(parent)
        self.theme = theme
        self.background_pixmap = None
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.reload_background()

    def reload_background(self):
        from core.widgets import load_background_image
        self.background_pixmap = load_background_image(self.theme)
        self.update()

    def set_theme(self, theme):
        self.theme = theme
        self.reload_background()

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        widget_rect = self.rect()

        clip_path = QPainterPath()
        clip_path.addRoundedRect(
            widget_rect.x(), widget_rect.y(),
            widget_rect.width(), widget_rect.height(),
            8, 8,
        )
        painter.setClipPath(clip_path)

        if self.background_pixmap and not self.background_pixmap.isNull():
            scaled = self.background_pixmap.scaled(
                widget_rect.size(),
                Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation,
            )
            x = widget_rect.width() - scaled.width()
            y = widget_rect.height() - scaled.height()
            painter.drawPixmap(x, y, scaled)
        else:
            fallback = QColor("#101412" if self.theme == "dark" else "#eef2f0")
            painter.fillRect(widget_rect, fallback)


class TemplateMainWindow(QMainWindow):
    """模板主窗口"""
    
    def __init__(self):
        super().__init__()
        
        # Keep the frameless shell, but use opaque surfaces for reliable contrast.
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)

        from core.styles import get_icon
        self.setWindowIcon(get_icon("logo"))

        # Select a valid legacy profile, or create one implicit local profile.
        current_user = config.get_current_user()
        existing_users = config.get_users()
        if current_user not in existing_users:
            if existing_users:
                config.switch_user(next(iter(existing_users)))
            else:
                config.add_user("本地档案", {"nickname": "本地档案"})

        self.setup_modern_ui()
        self.init_ui()

        self.setup_data_sharing()
        self.setup_update_check()
        self.setup_data_freshness_check()

    def setup_modern_ui(self):
        """设置现代化UI样式"""
        self.setStyleSheet(get_main_window_style(config.theme))

    def init_ui(self):
        """Build the navigation shell and attach the existing feature pages."""
        self.setWindowTitle("鸣潮成就管理器")
        self.resize(1320, 840)
        self.setMinimumSize(1080, 700)

        root = MainBackgroundWidget(config.theme)
        root.setObjectName("appRoot")
        self.setCentralWidget(root)

        root_layout = QVBoxLayout(root)
        root_layout.setContentsMargins(0, 0, 0, 0)
        root_layout.setSpacing(0)

        from core.custom_title_bar import CustomTitleBar
        self.title_bar = CustomTitleBar(self, show_theme_toggle=True)
        root_layout.addWidget(self.title_bar)

        body = QWidget()
        body_layout = QHBoxLayout(body)
        body_layout.setContentsMargins(0, 0, 0, 0)
        body_layout.setSpacing(0)
        root_layout.addWidget(body, 1)

        sidebar = QFrame()
        sidebar.setObjectName("appSidebar")
        sidebar.setFixedWidth(210)
        sidebar_layout = QVBoxLayout(sidebar)
        sidebar_layout.setContentsMargins(0, 18, 0, 14)
        sidebar_layout.setSpacing(4)

        brand = QWidget()
        brand_layout = QHBoxLayout(brand)
        brand_layout.setContentsMargins(18, 0, 16, 16)
        brand_layout.setSpacing(10)
        logo = QLabel()
        logo.setFixedSize(34, 34)
        if not self.windowIcon().isNull():
            logo.setPixmap(self.windowIcon().pixmap(32, 32))
        brand_layout.addWidget(logo)
        brand_text = QWidget()
        brand_text_layout = QVBoxLayout(brand_text)
        brand_text_layout.setContentsMargins(0, 0, 0, 0)
        brand_text_layout.setSpacing(1)
        brand_title = QLabel("鸣潮成就")
        brand_title.setObjectName("brandTitle")
        brand_subtitle = QLabel("ACHIEVEMENT DESK")
        brand_subtitle.setObjectName("brandSubtitle")
        brand_text_layout.addWidget(brand_title)
        brand_text_layout.addWidget(brand_subtitle)
        brand_layout.addWidget(brand_text, 1)
        sidebar_layout.addWidget(brand)

        section_label = QLabel("工作区")
        section_label.setObjectName("sidebarSection")
        section_label.setContentsMargins(18, 6, 0, 5)
        sidebar_layout.addWidget(section_label)

        self.nav_group = QButtonGroup(self)
        self.nav_group.setExclusive(True)
        self.nav_buttons = []
        nav_items = [
            ("成就管理", "浏览、筛选与维护进度"),
            ("统计分析", "完成率与分类分布"),
            ("数据获取", "同步与导入导出"),
            ("OCR 扫描", "从游戏画面识别进度"),
        ]
        for index, (label, tooltip) in enumerate(nav_items):
            button = self._create_nav_button(label, tooltip, index)
            sidebar_layout.addWidget(button)
            self.nav_buttons.append(button)
        sidebar_layout.addStretch()

        from version import VERSION
        version_label = QLabel(f"本地数据工具  ·  v{VERSION}")
        version_label.setObjectName("brandSubtitle")
        version_label.setContentsMargins(18, 0, 0, 2)
        sidebar_layout.addWidget(version_label)
        body_layout.addWidget(sidebar)

        content = QFrame()
        content.setObjectName("contentArea")
        content_layout = QVBoxLayout(content)
        content_layout.setContentsMargins(0, 0, 0, 0)
        self.page_stack = QStackedWidget()
        content_layout.addWidget(self.page_stack)
        body_layout.addWidget(content, 1)

        from core.manage_tab import ManageTab
        from core.statistics_tab import StatisticsTab
        from core.crawl_tab import CrawlTab
        from core.ocr_tab import OCRScanTab
        self.manage_tab = ManageTab()
        self.statistics_tab = StatisticsTab()
        self.crawl_tab = CrawlTab()
        self.ocr_tab = OCRScanTab()
        for page in (self.manage_tab, self.statistics_tab, self.crawl_tab, self.ocr_tab):
            self.page_stack.addWidget(page)

        self.nav_buttons[0].setChecked(True)
        self.page_stack.setCurrentIndex(0)

        signal_bus.settings_changed.connect(self.on_settings_saved)
        signal_bus.theme_changed.connect(self.apply_theme)
        signal_bus.category_config_updated.connect(self.on_category_config_updated)

    def _create_nav_button(self, label, tooltip, index):
        button = QPushButton(label)
        button.setObjectName("navButton")
        button.setCheckable(True)
        button.setCursor(Qt.CursorShape.PointingHandCursor)
        button.setToolTip(tooltip)
        button.clicked.connect(lambda checked=False, page=index: self.set_current_page(page))
        self.nav_group.addButton(button, index)
        return button

    def set_current_page(self, index):
        if 0 <= index < self.page_stack.count():
            self.page_stack.setCurrentIndex(index)
            self.nav_buttons[index].setChecked(True)
    
    def on_settings_saved(self, settings):
        """设置保存回调"""
        signal_bus.log_message.emit("SUCCESS", "设置已保存", {})
        # 更新主题和背景图片
        self.apply_theme()

    def apply_theme(self, *_args):
        """Apply the selected theme without rebuilding the page hierarchy."""
        self.setStyleSheet(get_main_window_style(config.theme))

        root = self.centralWidget()
        if isinstance(root, MainBackgroundWidget):
            root.set_theme(config.theme)

        if hasattr(self, "title_bar"):
            self.title_bar.update_theme()

        for page_name in ("manage_tab", "statistics_tab", "crawl_tab", "ocr_tab"):
            page = getattr(self, page_name, None)
            if page is not None and hasattr(page, "apply_theme"):
                page.apply_theme(config.theme)

    def setup_data_sharing(self):
            """设置数据共享机制"""
            # 监听爬虫完成信号
            if hasattr(self, 'crawl_tab'):
                # 连接爬虫完成信号到管理标签页
                from PySide6.QtCore import QTimer
                # 使用定时器延迟连接，确保组件已完全初始化
                QTimer.singleShot(100, self._connect_crawler_signal)
    
    def _connect_crawler_signal(self):
        """连接爬虫信号"""
        # 爬虫完成后不需要切换标签页，所以不需要连接信号
        pass
    
    def setup_update_check(self):
        """设置更新检查"""
        # 检查并清理可能存在的过期缓存
        self._clean_update_cache_if_needed()
        
        # 连接更新检查信号
        signal_bus.update_available.connect(self.on_update_available)
        
        # 延迟3秒后进行后台更新检查，避免影响启动速度
        from PySide6.QtCore import QTimer
        QTimer.singleShot(3000, self._delayed_update_check)
    
    def _clean_update_cache_if_needed(self):
        """如果需要，清理更新缓存"""
        import json
        from pathlib import Path
        from version import VERSION
        
        cache_file = Path("resources/update_cache.json")
        
        # 如果缓存文件存在，检查版本信息
        if cache_file.exists():
            try:
                with open(cache_file, 'r', encoding='utf-8') as f:
                    cache_data = json.load(f)
                
                # 获取缓存中的版本信息
                update_info = cache_data.get('update_info', {})
                cached_current_version = update_info.get('current_version', '')
                
                # 如果当前版本与缓存中的版本不一致，说明软件已更新
                if cached_current_version and cached_current_version != VERSION:
                    logger.info("检测到版本更新: %s -> %s, 清理更新缓存", cached_current_version, VERSION)
                    cache_file.unlink()  # 删除缓存文件

            except (json.JSONDecodeError, KeyError) as e:
                logger.info("读取缓存文件失败，删除缓存: %s", e)
                # 如果缓存文件损坏，直接删除
                if cache_file.exists():
                    cache_file.unlink()
    
    def _delayed_update_check(self):
        """延迟的更新检查，避免影响启动速度"""
        try:
            from core.update import check_for_updates_background
            check_for_updates_background()
        except Exception as e:
            logger.error("延迟更新检查失败: %s", e)

    def setup_data_freshness_check(self):
        """启动时静默检查成就数据是否有更新，并自动合并缺失成就"""
        from PySide6.QtCore import QTimer
        QTimer.singleShot(5000, self._check_data_freshness)

    def _check_data_freshness(self):
        """后台检查成就数据并自动合并"""
        from core.crawl_tab import AchievementCrawler, CrawlerThread

        self._sync_crawler = AchievementCrawler(target_version=None)
        self._sync_crawler.progress.connect(
            lambda msg: logger.info("%s", msg)
        )
        self._sync_crawler.finished.connect(self._on_sync_finished)
        self._sync_crawler.error.connect(
            lambda err: logger.error("检查失败: %s", err)
        )

        self._sync_thread = CrawlerThread(self._sync_crawler)
        self._sync_crawler.crawl = self._sync_crawl
        self._sync_thread.start()

    def _sync_crawl(self):
        """获取远端全量数据，解析所有成就（不筛选版本），与本地对比合并"""
        import re
        try:
            self._sync_crawler.progress.emit("正在检查成就数据更新...")
            api_data = self._sync_crawler.get_achievement_data()
            if not api_data:
                self._sync_crawler.error.emit("获取数据失败")
                return

            # 解析全量成就（不筛选版本）
            all_remote = []
            content = api_data.get('data', {}).get('content', {})
            modules = content.get('modules', [])
            for module in modules:
                for component in module.get('components', []):
                    if component.get('type') == 'filter-component':
                        html_content = component.get('content', '')
                        parsed = self._sync_crawler.parse_html_table_with_categories(html_content)
                        all_remote.extend(parsed)

            logger.info("远端共 %s 条成就", len(all_remote))

            # 加载本地成就库
            local_achievements = config.load_base_achievements()
            logger.info("本地共 %s 条成就", len(local_achievements))

            # 用 (名称, 清理后描述) 去重
            def clean_desc(desc):
                if not desc:
                    return desc
                return re.sub(r'[.,…。，；：！？、]+$', '', desc).strip()

            local_keys = set()
            for a in local_achievements:
                key = (a.get('名称', ''), clean_desc(a.get('描述', '')))
                local_keys.add(key)

            remote_keys = set()
            to_add = []
            for a in all_remote:
                key = (a.get('名称', ''), clean_desc(a.get('描述', '')))
                remote_keys.add(key)
                if key not in local_keys:
                    to_add.append(a)

            # 检测本地有但远端已移除的成就
            to_remove_keys = local_keys - remote_keys
            if to_remove_keys:
                removed_names = [name for name, _ in to_remove_keys]
                logger.info("发现 %s 条成就已从远端移除: %s", len(to_remove_keys), ', '.join(removed_names))

            # 保存远端 keys 供主线程使用
            self._sync_remote_keys = remote_keys

            if not to_add and not to_remove_keys:
                logger.info("本地数据已是最新，无需同步")
                self._sync_remote_keys = None
                self._sync_crawler.finished.emit([])
                return

            # 按版本统计新增数量
            if to_add:
                version_counts = {}
                for a in to_add:
                    v = a.get('版本', '未知')
                    version_counts[v] = version_counts.get(v, 0) + 1
                version_summary = ", ".join(f"v{v}: {c}条" for v, c in sorted(version_counts.items()))
                logger.info("发现 %s 条新成就需要同步（%s）", len(to_add), version_summary)

            # 发出新增成就列表（可能为空但有需要移除的），由主线程处理
            self._sync_crawler.finished.emit(to_add)

        except Exception as e:
            self._sync_crawler.error.emit(str(e))

    def _on_sync_finished(self, new_achievements):
        """同步完成，在主线程中合并新成就到成就库"""
        import re

        remote_keys = getattr(self, '_sync_remote_keys', None)
        has_changes = bool(new_achievements) or bool(remote_keys)

        if not has_changes:
            logger.info("启动数据检查完成，无新数据")
            self._sync_crawler = None
            self._sync_thread = None
            return

        logger.info("正在同步数据...")

        manage_tab = self.manage_tab
        current_achievements = manage_tab.manager.achievements

        # 检测新分类
        category_config = config.load_category_config()
        first_categories = category_config.get("first_categories", {})
        second_categories = category_config.get("second_categories", {})
        updated_first = first_categories.copy()
        updated_second = {k: v.copy() for k, v in second_categories.items()}
        has_new_categories = False

        for achievement in new_achievements:
            first_cat = achievement.get('第一分类', '')
            second_cat = achievement.get('第二分类', '')
            if first_cat and second_cat:
                if first_cat not in updated_first:
                    max_order = max(updated_first.values()) if updated_first else 0
                    updated_first[first_cat] = max_order + 1
                    updated_second[first_cat] = {}
                    has_new_categories = True
                    logger.info("新增第一分类: %s", first_cat)

                if first_cat not in updated_second:
                    updated_second[first_cat] = {}
                if second_cat not in updated_second[first_cat]:
                    existing = set()
                    for s in updated_second[first_cat].values():
                        try:
                            existing.add(int(s))
                        except (ValueError, TypeError):
                            pass
                    new_suffix = 10
                    while new_suffix in existing:
                        new_suffix += 10
                    updated_second[first_cat][second_cat] = str(new_suffix)
                    has_new_categories = True
                    logger.info("新增第二分类: %s - %s", first_cat, second_cat)

        if has_new_categories:
            config.save_category_config({
                "first_categories": updated_first,
                "second_categories": updated_second
            })
            logger.info("分类配置已更新")

        # 移除远端已删除的成就
        removed_count = 0
        if remote_keys:
            def clean_desc(desc):
                if not desc:
                    return desc
                return re.sub(r'[.,…。，；：！？、]+$', '', desc).strip()

            before_count = len(current_achievements)
            current_achievements = [
                a for a in current_achievements
                if (a.get('名称', ''), clean_desc(a.get('描述', ''))) in remote_keys
            ]
            removed_count = before_count - len(current_achievements)
            if removed_count:
                logger.info("已移除 %s 条远端已删除的成就", removed_count)

        # 合并新增成就并重新编码
        all_achievements = current_achievements + new_achievements
        all_achievements, _ = manage_tab._smart_reencode_achievements(all_achievements)

        # 更新管理器数据
        manage_tab.manager.achievements = all_achievements
        manage_tab.manager.filtered_achievements = all_achievements.copy()

        # 先持久化（save_to_json 会写入 base_achievements.json）
        manage_tab.save_to_json()

        # 再重新编码用户存档（从文件重新加载，所以必须先保存）
        if config.reencode_all_user_progress():
            logger.info("用户存档数据已同步")

        if has_new_categories:
            signal_bus.category_config_updated.emit()

        # 重新加载数据到管理页（确保 UI 显示与文件一致）
        manage_tab.load_local_data()

        # 输出同步摘要
        summary_parts = []
        if new_achievements:
            version_counts = {}
            for a in new_achievements:
                v = a.get('版本', '未知')
                version_counts[v] = version_counts.get(v, 0) + 1
            version_summary = ", ".join(f"v{v}: {c}条" for v, c in sorted(version_counts.items()))
            summary_parts.append(f"新增 {len(new_achievements)} 条（{version_summary}）")
        if removed_count:
            summary_parts.append(f"移除 {removed_count} 条")
        logger.info("同步完成！%s，总计 %s 条", ', '.join(summary_parts), len(all_achievements))

        # 清理引用
        self._sync_crawler = None
        self._sync_thread = None
        self._sync_remote_keys = None

    def on_update_available(self, update_info):
        """处理可用更新"""
        from core.update_dialog import UpdateDialog
        
        # 创建自定义更新对话框
        dialog = UpdateDialog(self, update_info)
        
        # 显示对话框并等待用户响应
        if dialog.exec() == QDialog.Accepted:
            # 用户点击了确认，密码已复制，链接已打开
            pass
    
    def on_category_config_updated(self):
        """处理分类配置更新"""
        # 重新加载成就管理标签页的数据
        if hasattr(self, 'manage_tab') and hasattr(self.manage_tab, 'load_local_data'):
            self.manage_tab.load_local_data()
            logger.info("成就管理数据已重新加载")

    def show_first_run_dialog(self):
        """显示首次运行欢迎对话框"""
        from PySide6.QtWidgets import QDialog, QVBoxLayout, QHBoxLayout, QLabel, QPushButton
        from PySide6.QtCore import Qt
        from core.styles import get_dialog_style
        from core.widgets import BackgroundWidget, load_background_image
        from core.custom_title_bar import CustomTitleBar
        
        dialog = QDialog(self)
        dialog.setWindowTitle("欢迎使用鸣潮成就管理器")
        dialog.setFixedSize(600, 500)
        dialog.setModal(False)  # 设置为非模态，允许主窗口同时显示
        
        # 设置无边框窗口和透明背景以实现圆角
        dialog.setWindowFlags(Qt.WindowType.Dialog | Qt.WindowType.FramelessWindowHint)
        dialog.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        dialog.setStyleSheet(get_dialog_style(config.theme))

        # 背景图片初始化
        background_pixmap = load_background_image(config.theme)
        
        # 创建主布局（透明）
        main_layout = QVBoxLayout(dialog)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)
        
        # 创建容器（用于绘制背景）
        container_widget = BackgroundWidget(background_pixmap, config.theme)
        container_widget.setObjectName("dialogContainer")
        container_layout = QVBoxLayout(container_widget)
        container_layout.setContentsMargins(0, 0, 0, 0)
        container_layout.setSpacing(0)
        main_layout.addWidget(container_widget)
        
        # 添加自定义标题栏（不显示主题切换按钮）
        title_bar = CustomTitleBar(dialog, show_theme_toggle=False)
        container_layout.addWidget(title_bar)
        
        # 内容区域
        content_widget = QWidget()
        layout = QVBoxLayout(content_widget)
        container_layout.addWidget(content_widget)
        
        layout.setSpacing(15)
        
        # 标题
        title_label = QLabel("🎊 欢迎使用鸣潮成就管理器！")
        title_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title_label.setStyleSheet("font-size: 18px; font-weight: bold; color: #3498db; margin: 10px;")
        layout.addWidget(title_label)
        
        # 说明文本 - 使用QLabel和HTML格式，与帮助对话框保持一致
        info_text = QLabel()
        info_text.setWordWrap(True)
        info_text.setTextFormat(Qt.TextFormat.RichText)
        info_text.setOpenExternalLinks(True)
        info_text.setText("""
        <p><b>📖 快速入门指南：</b></p>
        <p style='margin-left: 20px;'>1. <b>添加用户</b>：首先需要在设置中添加您的游戏昵称和uid</p>
        <p style='margin-left: 20px;'>2. <b>数据爬取</b>：输入版本号同步对应版本的成就数据</p>
        <p style='margin-left: 20px;'>3. <b>管理成就</b>：在成就管理中查看和标记您的成就进度</p>
        
        <p><b>💡 使用提示：</b></p>
        <p style='margin-left: 20px;'>• 点击左上角头像可以切换角色形象</p>
        <p style='margin-left: 20px;'>• 设置→分类管理可以自定义分类排序</p>
        <p style='margin-left: 20px;'>• 所有数据都保存在本地，安全可靠</p>
        
        <p><b>❓ 需要帮助？</b></p>
        <p style='margin-left: 20px;'>点击右下角"帮助"按钮查看详细使用说明</p>
        """)
        layout.addWidget(info_text)
        
        # 按钮区域
        button_layout = QHBoxLayout()
        button_layout.addStretch()
        
        help_btn = QPushButton("查看帮助")
        help_btn.clicked.connect(lambda: self.show_help_dialog())
        help_btn.setMinimumWidth(100)
        
        ok_btn = QPushButton("开始使用")
        ok_btn.clicked.connect(dialog.accept)
        ok_btn.setMinimumWidth(100)
        ok_btn.setDefault(True)
        
        button_layout.addWidget(help_btn)
        button_layout.addWidget(ok_btn)
        layout.addLayout(button_layout)
        
        # 应用样式
        from core.styles import get_button_style
        help_btn.setStyleSheet(get_button_style(config.theme))
        ok_btn.setStyleSheet(get_button_style(config.theme))
        
        # 应用帮助文本样式
        from core.styles import get_help_text_style
        info_text.setStyleSheet(get_help_text_style(config.theme))
        
        # 显示对话框（非阻塞）
        dialog.show()
        
        # 保存配置，标记不是首次运行
        config.save_config()
        
        # 对话框关闭时自动删除
        dialog.finished.connect(lambda: dialog.deleteLater())
    
    def show_help_dialog(self):
        """显示帮助对话框"""
        from core.help_dialog import HelpDialog
        help_dialog = HelpDialog(self)
        help_dialog.show()

    def closeEvent(self, event):
            """窗口关闭事件"""
            event.accept()
