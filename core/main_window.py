import os
from PySide6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                               QTabWidget, QDialog)
from PySide6.QtCore import Qt, QTimer
from PySide6.QtGui import QPixmap

from core.config import config
from core.signal_bus import signal_bus

from core.styles import get_main_window_style, ColorPalette
from core.widgets import BackgroundWidget, load_background_image
from core.circular_avatar import CircularAvatar
from core.avatar_selector import AvatarSelector


class TemplateMainWindow(QMainWindow):
    """模板主窗口"""
    
    def __init__(self):
        super().__init__()
        
        # 设置无边框窗口
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint)
        # 设置窗口透明以显示圆角
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        
        # 设置窗口图标
        from core.styles import get_icon
        self.setWindowIcon(get_icon("logo"))
        
        # 加载背景图片
        self.background_pixmap = load_background_image(config.theme)

        # 设置现代UI样式
        self.setup_modern_ui()
        self.init_ui()

        # 应用滚动条样式
        from core.styles import get_scrollbar_style
        self.setStyleSheet(self.styleSheet() + get_scrollbar_style(config.theme))
        
        
        # 连接数据共享信号
        self.setup_data_sharing()
        
        # 检查是否首次运行
        if config.first_run:
            self.show_first_run_dialog()
        
        # 启动时检查更新（后台进行）
        self.setup_update_check()

        # 启动时静默检查成就数据更新
        self.setup_data_freshness_check()

    def setup_modern_ui(self):
        """设置现代化UI样式"""
        self.setStyleSheet(get_main_window_style(config.theme))

    def init_ui(self):
        """初始化UI"""
        self.setWindowTitle("鸣潮成就管理器")
        self.setGeometry(100, 100, 1200, 800)

        # 创建带背景图片的中心widget
        central_widget = BackgroundWidget(self.background_pixmap, config.theme)
        self.setCentralWidget(central_widget)

        # 主布局（垂直，包含标题栏和内容）
        main_container_layout = QVBoxLayout(central_widget)
        main_container_layout.setContentsMargins(0, 0, 0, 0)
        main_container_layout.setSpacing(0)
        
        # 添加自定义标题栏（主窗口显示主题切换按钮）
        from core.custom_title_bar import CustomTitleBar
        self.title_bar = CustomTitleBar(self, show_theme_toggle=True)
        main_container_layout.addWidget(self.title_bar)
        
        # 内容区域
        content_widget = QWidget()
        main_container_layout.addWidget(content_widget)
        
        main_layout = QHBoxLayout(content_widget)

        # 左侧栏
        left_widget = QWidget()
        left_widget.setMinimumWidth(250)
        left_widget.setMaximumWidth(250)
        left_layout = QVBoxLayout(left_widget)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(0)
        left_layout.setSizeConstraint(QVBoxLayout.SizeConstraint.SetNoConstraint)

        # 头像和设置区域
        avatar_widget = QWidget()
        avatar_layout = QVBoxLayout(avatar_widget)
        avatar_layout.setContentsMargins(10, 10, 10, 10)
        avatar_layout.setSpacing(8)
        avatar_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        
        # 头像
        self.avatar_label = CircularAvatar(size=100)
        self.avatar_label.setCursor(Qt.CursorShape.PointingHandCursor)
        avatar_layout.addWidget(self.avatar_label, alignment=Qt.AlignmentFlag.AlignCenter)
        
        # 昵称标签
        self.nickname_label = QLabel("")
        self.nickname_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.update_nickname_style()
        avatar_layout.addWidget(self.nickname_label, alignment=Qt.AlignmentFlag.AlignCenter)
        
        left_layout.addWidget(avatar_widget)
        
        # 创建头像选择器窗口但不显示
        self.avatar_selector = AvatarSelector()
        self.setup_avatar_signals()
        
        left_layout.addStretch()
        
        main_layout.addWidget(left_widget)

        # 右侧操作区域
        right_widget = QWidget()
        right_layout = QVBoxLayout(right_widget)

        # 创建标签页
        self.tab_widget = QTabWidget()

        # 添加成就管理标签页
        from core.manage_tab import ManageTab
        self.manage_tab = ManageTab()
        self.tab_widget.addTab(self.manage_tab, "🏆 成就管理")

        # 添加统计信息标签页
        from core.statistics_tab import StatisticsTab
        self.statistics_tab = StatisticsTab()
        self.tab_widget.addTab(self.statistics_tab, "📈 统计图表")
        
        # 添加数据爬取标签页
        from core.crawl_tab import CrawlTab
        self.crawl_tab = CrawlTab()
        self.tab_widget.addTab(self.crawl_tab, "📊 数据爬取")

        # 应用滚动条样式到标签页
        from core.styles import get_scrollbar_style
        self.tab_widget.setStyleSheet(self.tab_widget.styleSheet() + get_scrollbar_style(config.theme))

        right_layout.addWidget(self.tab_widget)

        # 创建角色立绘标签，固定在左下角（背景层级）
        self.character_portrait_label = QLabel(central_widget)
        self.character_portrait_label.setFixedSize(500, 500)
        self.character_portrait_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        # 降低层级，让其他组件显示在上方
        self.character_portrait_label.lower()
        # 使用定时器在窗口显示后更新位置
        QTimer.singleShot(0, self.position_character_portrait)
        
        # 从配置加载当前用户的角色立绘
        character_name = config.get_current_user_character_name()
        self.update_character_portrait(character_name)
        
        main_layout.addWidget(right_widget)
        
        # 确保角色立绘在最底层
        self.character_portrait_label.lower()

        # 连接信号
        signal_bus.settings_changed.connect(self.on_settings_saved)
        signal_bus.theme_changed.connect(self.apply_theme)
        signal_bus.category_config_updated.connect(self.on_category_config_updated)
        
        # 初始化头像和昵称显示
        self.update_nickname_display()
        self.update_avatar_display()
    
    def position_character_portrait(self):
        """定位角色立绘到左下角"""
        # 获取标题栏高度
        title_bar_height = self.title_bar.height() if hasattr(self, 'title_bar') else 0
        # 定位到左下角（考虑标题栏高度）
        x = -100  # 向左偏移100px，部分超出窗口
        y = self.height() - 500 - 20  # 距离底部20px
        self.character_portrait_label.move(x, y)
        # 确保图片始终在最底层
        self.character_portrait_label.lower()
    
    def resizeEvent(self, event):
        """窗口大小改变时重新定位角色立绘"""
        super().resizeEvent(event)
        if hasattr(self, 'character_portrait_label'):
            self.position_character_portrait()



    def setup_avatar_signals(self):
            """设置头像相关信号"""
            self.avatar_label.mousePressEvent = self.on_avatar_clicked
            # 连接头像选择器的信号
            self.avatar_selector.avatar_selected.connect(self.on_avatar_selected)
            # 监听用户切换信号
            signal_bus.user_switched.connect(self.on_user_switched)
            # 初始化昵称和头像显示
            self.update_nickname_display()
            self.update_avatar_display()    
    def on_user_switched(self, username):
        """用户切换时的处理"""
        self.update_nickname_display()
        self.update_avatar_display()
        character_name = config.get_current_user_character_name()
        print(f"[DEBUG] 用户切换: {username}, 角色名: {character_name}")
        self.update_character_portrait(character_name)
    
    def update_nickname_display(self):
        """更新昵称显示"""
        current_user = config.get_current_user()
        users = config.get_users()
        current_data = users.get(current_user, {})
        
        if isinstance(current_data, dict):
            nickname = current_data.get('nickname', current_user)
        else:
            nickname = current_user
            
        self.nickname_label.setText(nickname)
        self.update_nickname_style()
    
    def update_nickname_style(self):
        """更新昵称样式"""
        colors = ColorPalette.Dark if config.theme == "dark" else ColorPalette.Light
        text_color = colors.TEXT_PRIMARY
        self.nickname_label.setStyleSheet(f"font-size: 14px; font-weight: bold; color: {text_color}; margin-left: 10px;")
    
    def update_avatar_display(self):
        """更新头像显示"""
        avatar_path = config.get_current_user_avatar()
        print(f"[DEBUG] 更新头像显示: {avatar_path}")
        if avatar_path:
            print(f"[DEBUG] 找到头像文件: {os.path.exists(avatar_path)}")
            self.avatar_label.update_avatar(avatar_path)
        else:
            print("[DEBUG] 没有找到用户头像，使用默认头像")
            # 使用默认头像（男漂泊者）
            default_avatar = os.path.join("resources", "profile", "男漂泊者.png")
            self.avatar_label.update_avatar(default_avatar)
    
    def on_avatar_clicked(self, event):
        """头像点击事件"""
        if event.button() == Qt.MouseButton.LeftButton:
            # 显示头像选择器
            self.avatar_selector.show()
            # 将选择器窗口置于主窗口中央
            self.avatar_selector.move(
                self.geometry().center() - self.avatar_selector.rect().center()
            )
    
    def on_avatar_selected(self, avatar_path, avatar_name):
        """处理头像选择信号"""
        print(f"[DEBUG] 收到头像选择信号: {avatar_path} - {avatar_name}")
        # 更新头像
        self.avatar_label.update_avatar(avatar_path)
        
        # 更新角色立绘
        self.update_character_portrait(avatar_name)
        
        # 保存到当前用户的头像配置
        current_user = config.get_current_user()
        print(f"[DEBUG] 当前用户: {current_user}")
        config.set_user_avatar(current_user, avatar_path)
        config.set_user_character_name(current_user, avatar_name)
        print(f"[DEBUG] 头像已保存到配置: {config.get_current_user_avatar()}")
        print(f"[DEBUG] 角色名已保存到配置: {avatar_name}")
        
        # 发送日志消息
        signal_bus.log_message.emit("INFO", f"已选择头像: {avatar_name}", {})
    
    def update_character_portrait(self, character_name):
        """更新角色立绘"""
        from core.config import get_resource_path
        
        # 尝试查找角色立绘文件（支持 png 和 webp 格式）
        characters_dir = get_resource_path("resources/characters")
        portrait_path = None
        
        if characters_dir.exists():
            # 优先尝试 webp 格式
            webp_path = characters_dir / f"{character_name}.webp"
            png_path = characters_dir / f"{character_name}.png"
            
            if webp_path.exists():
                portrait_path = webp_path
            elif png_path.exists():
                portrait_path = png_path
        
        # 加载并显示图片
        if portrait_path and portrait_path.exists():
            portrait_pixmap = QPixmap(str(portrait_path))
            if not portrait_pixmap.isNull():
                # 设置图片大小为500x500，保持宽高比
                scaled_pixmap = portrait_pixmap.scaled(
                    500, 500,
                    Qt.AspectRatioMode.KeepAspectRatio,
                    Qt.TransformationMode.SmoothTransformation
                )
                self.character_portrait_label.setPixmap(scaled_pixmap)
                print(f"[DEBUG] 已更新角色立绘: {character_name}")
            else:
                print(f"[WARNING] 无法加载角色立绘: {portrait_path}")
                self.character_portrait_label.clear()
        else:
            print(f"[WARNING] 未找到角色立绘: {character_name}")
            self.character_portrait_label.clear()

    def on_settings_saved(self, settings):
        """设置保存回调"""
        signal_bus.log_message.emit("SUCCESS", "设置已保存", {})
        # 更新主题和背景图片
        self.apply_theme()

    def apply_theme(self):
        """应用主题到所有组件"""
        
        # 更新背景图片
        self.background_pixmap = load_background_image(config.theme)
        central = self.centralWidget()
        if isinstance(central, BackgroundWidget):
            central.set_background(self.background_pixmap, config.theme)
        
        # 更新自定义标题栏主题
        if hasattr(self, 'title_bar'):
            self.title_bar.update_theme()
        
        self.setStyleSheet(get_main_window_style(config.theme))
        
        # 更新头像边框颜色
        if hasattr(self, 'avatar_label'):
            self.avatar_label.apply_theme(config.theme)
        
        # 更新昵称样式
        if hasattr(self, 'nickname_label'):
            self.update_nickname_style()
        
        # 数据爬取标签页
        if hasattr(self, 'crawl_tab'):
            if hasattr(self.crawl_tab, 'apply_theme'):
                self.crawl_tab.apply_theme(config.theme)
        
        # 成就管理标签页
        if hasattr(self, 'manage_tab'):
            if hasattr(self.manage_tab, 'apply_theme'):
                self.manage_tab.apply_theme(config.theme)
        
        for i in range(self.findChildren(QWidget).__len__()):
            widget = self.findChildren(QWidget)[i]
            if hasattr(widget, 'apply_theme'):
                widget.apply_theme(config.theme)

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
                    print(f"检测到版本更新: {cached_current_version} -> {VERSION}, 清理更新缓存")
                    cache_file.unlink()  # 删除缓存文件
                    
            except (json.JSONDecodeError, KeyError) as e:
                print(f"读取缓存文件失败，删除缓存: {e}")
                # 如果缓存文件损坏，直接删除
                if cache_file.exists():
                    cache_file.unlink()
    
    def _delayed_update_check(self):
        """延迟的更新检查，避免影响启动速度"""
        try:
            from core.update import check_for_updates_background
            check_for_updates_background()
        except Exception as e:
            print(f"延迟更新检查失败: {e}")

    def setup_data_freshness_check(self):
        """启动时静默检查成就数据是否有更新，并自动合并缺失成就"""
        from PySide6.QtCore import QTimer
        QTimer.singleShot(5000, self._check_data_freshness)

    def _check_data_freshness(self):
        """后台检查成就数据并自动合并"""
        from core.crawl_tab import AchievementCrawler, CrawlerThread

        devcode, token = config.get_auth_data()
        if not devcode or not token:
            print("[DATA-CHECK] 认证信息未配置，跳过启动数据检查")
            return

        self._sync_crawler = AchievementCrawler(target_version=None)
        self._sync_crawler.progress.connect(
            lambda msg: print(f"[DATA-CHECK] {msg}")
        )
        self._sync_crawler.finished.connect(self._on_sync_finished)
        self._sync_crawler.error.connect(
            lambda err: print(f"[DATA-CHECK] 检查失败: {err}")
        )

        self._sync_thread = CrawlerThread(self._sync_crawler)
        self._sync_crawler.crawl = self._sync_crawl
        self._sync_thread.start()

    def _sync_crawl(self):
        """获取远端全量数据，解析所有成就（不筛选版本），与本地对比合并"""
        import re
        try:
            self._sync_crawler._load_auth_config()
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

            print(f"[DATA-CHECK] 远端共 {len(all_remote)} 条成就")

            # 加载本地成就库
            local_achievements = config.load_base_achievements()
            print(f"[DATA-CHECK] 本地共 {len(local_achievements)} 条成就")

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
                print(f"[DATA-CHECK] 发现 {len(to_remove_keys)} 条成就已从远端移除: {', '.join(removed_names)}")

            # 保存远端 keys 供主线程使用
            self._sync_remote_keys = remote_keys

            if not to_add and not to_remove_keys:
                print("[DATA-CHECK] 本地数据已是最新，无需同步")
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
                print(f"[DATA-CHECK] 发现 {len(to_add)} 条新成就需要同步（{version_summary}）")

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
            print("[DATA-CHECK] 启动数据检查完成，无新数据")
            self._sync_crawler = None
            self._sync_thread = None
            return

        print(f"[DATA-CHECK] 正在同步数据...")

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
                    print(f"[DATA-CHECK] 新增第一分类: {first_cat}")

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
                    print(f"[DATA-CHECK] 新增第二分类: {first_cat} - {second_cat}")

        if has_new_categories:
            config.save_category_config({
                "first_categories": updated_first,
                "second_categories": updated_second
            })
            print("[DATA-CHECK] 分类配置已更新")

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
                print(f"[DATA-CHECK] 已移除 {removed_count} 条远端已删除的成就")

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
            print("[DATA-CHECK] 用户存档数据已同步")

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
        print(f"[DATA-CHECK] 同步完成！{', '.join(summary_parts)}，总计 {len(all_achievements)} 条")

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
            print("[INFO] 成就管理数据已重新加载")

    def show_first_run_dialog(self):
        """显示首次运行欢迎对话框"""
        from PySide6.QtWidgets import QDialog, QVBoxLayout, QHBoxLayout, QLabel, QPushButton, QTextEdit
        from PySide6.QtCore import Qt
        from core.styles import get_dialog_style, get_scrollbar_style
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
        <p style='margin-left: 20px;'>2. <b>设置认证信息</b>：在设置-用户管理-通用认证设置查看如何设置</p>
        <p style='margin-left: 20px;'>3. <b>数据爬取</b>：输入版本号爬取对应版本的成就数据</p>
        <p style='margin-left: 20px;'>4. <b>管理成就</b>：在成就管理中查看和标记您的成就进度</p>
        
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
