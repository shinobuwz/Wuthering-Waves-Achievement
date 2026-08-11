"""
OCR 扫描标签页
提供游戏窗口检测、OCR 成就扫描、结果预览和进度更新功能
"""
from PySide6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                               QPushButton, QTableWidget, QTableWidgetItem,
                               QComboBox, QFrame, QHeaderView)
from PySide6.QtCore import QThread, Signal
from PySide6.QtGui import QColor

from core.config import config
from core.signal_bus import signal_bus


class OCRScanWorker(QThread):
    """OCR 扫描工作线程"""
    progress = Signal(dict, str, str)  # (进度dict, 一级Tab, 二级Tab)
    finished = Signal(dict)            # 最终进度dict {编号: {"获取状态": ...}}
    error = Signal(str)
    status = Signal(str)

    def __init__(self, hwnd, scan_mode="global"):
        super().__init__()
        self.hwnd = hwnd
        self.scan_mode = scan_mode  # "global" or "single"
        self._stop = False

    def stop(self):
        self._stop = True

    def run(self):
        try:
            self.status.emit("正在加载 OCR 模型...")
            from onnxocr import ONNXPaddleOcr
            ocr_model = ONNXPaddleOcr(use_angle_cls=False, use_gpu=False)

            self.status.emit("正在加载成就数据库...")
            achievements_db = config.load_base_achievements()
            if not achievements_db:
                self.error.emit("成就数据库为空，请先爬取成就数据")
                return

            def on_progress(progress_dict, primary, secondary):
                self.progress.emit(progress_dict, primary, secondary)

            if self.scan_mode == "single":
                self.status.emit("开始单页扫描...")
                from core.achievement_ocr import scan_current_page
                result = scan_current_page(
                    self.hwnd,
                    ocr_model,
                    achievements_db,
                    callback=on_progress,
                    stop_flag=lambda: self._stop,
                )
            else:
                self.status.emit("开始全量扫描...")
                from core.achievement_ocr import scan_all_tabs

                # 构建分类映射
                category_map = {}
                for a in achievements_db:
                    c1 = a.get('第一分类', '')
                    c2 = a.get('第二分类', '')
                    if c1 not in category_map:
                        category_map[c1] = []
                    if c2 not in category_map[c1]:
                        category_map[c1].append(c2)

                result = scan_all_tabs(
                    self.hwnd,
                    ocr_model,
                    achievements_db,
                    category_map,
                    callback=on_progress,
                    stop_flag=lambda: self._stop,
                )

            self.finished.emit(result)

        except Exception as e:
            self.error.emit(str(e))


class OCRScanTab(QWidget):
    """OCR 扫描标签页"""

    def __init__(self):
        super().__init__()
        self.hwnd = None
        self.worker = None
        self.scan_results = {}
        self.init_ui()

    def init_ui(self):
        """初始化 OCR 扫描工作区。"""
        from core.widgets import PageHeader

        layout = QVBoxLayout(self)
        layout.setContentsMargins(24, 20, 24, 18)
        layout.setSpacing(14)
        layout.addWidget(PageHeader(
            "OCR 扫描",
            "检测游戏窗口，扫描当前页面或自动遍历全部成就分类。",
        ))

        toolbar = QFrame()
        toolbar.setObjectName("toolbar")
        toolbar_layout = QVBoxLayout(toolbar)
        toolbar_layout.setContentsMargins(14, 11, 14, 11)
        toolbar_layout.setSpacing(8)

        control_row = QHBoxLayout()
        control_row.setSpacing(8)
        self.detect_btn = QPushButton("检测游戏窗口")
        self.detect_btn.clicked.connect(self.on_detect_window)
        control_row.addWidget(self.detect_btn)

        self.window_status_label = QLabel("尚未检测游戏窗口")
        self.window_status_label.setObjectName("pageSubtitle")
        control_row.addWidget(self.window_status_label, 1)

        control_row.addWidget(QLabel("扫描范围"))
        self.scan_mode_combo = QComboBox()
        self.scan_mode_combo.addItems(["全局扫描", "单页扫描"])
        self.scan_mode_combo.setCurrentIndex(0)
        self.scan_mode_combo.setMinimumWidth(110)
        control_row.addWidget(self.scan_mode_combo)

        self.scan_btn = QPushButton("开始扫描")
        self.scan_btn.setProperty("buttonRole", "primary")
        self.scan_btn.clicked.connect(self.on_scan_toggle)
        self.scan_btn.setEnabled(False)
        control_row.addWidget(self.scan_btn)
        toolbar_layout.addLayout(control_row)

        progress_row = QHBoxLayout()
        self.scan_status_label = QLabel("就绪")
        self.scan_status_label.setObjectName("sectionLabel")
        progress_row.addWidget(self.scan_status_label)
        progress_row.addStretch()
        self.progress_label = QLabel("已识别  0 条成就")
        self.progress_label.setObjectName("pageSubtitle")
        progress_row.addWidget(self.progress_label)
        toolbar_layout.addLayout(progress_row)
        layout.addWidget(toolbar)

        result_header = QHBoxLayout()
        result_title = QLabel("扫描结果")
        result_title.setObjectName("sectionLabel")
        result_header.addWidget(result_title)
        result_header.addStretch()
        self.save_btn = QPushButton("保存到当前进度")
        self.save_btn.setProperty("buttonRole", "primary")
        self.save_btn.clicked.connect(self.on_save_results)
        self.save_btn.setEnabled(False)
        result_header.addWidget(self.save_btn)
        layout.addLayout(result_header)

        self.result_table = QTableWidget()
        self.result_table.setColumnCount(4)
        self.result_table.setHorizontalHeaderLabels(["编号", "成就名称", "状态", "OCR 原文"])
        self.result_table.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        self.result_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.result_table.setAlternatingRowColors(True)
        header = self.result_table.horizontalHeader()
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.ResizeToContents)
        header.setSectionResizeMode(1, QHeaderView.ResizeMode.Stretch)
        header.setSectionResizeMode(2, QHeaderView.ResizeMode.ResizeToContents)
        header.setSectionResizeMode(3, QHeaderView.ResizeMode.Stretch)
        layout.addWidget(self.result_table, 1)

        self.apply_theme(config.theme)

    def on_detect_window(self):
        """检测游戏窗口"""
        try:
            from core.game_capture import find_game_window, get_window_rect
            self.hwnd = find_game_window()
            x, y, w, h = get_window_rect(self.hwnd)
            self.window_status_label.setText(
                f"已检测到窗口 (位置: {x},{y}  分辨率: {w}x{h})"
            )
            self.window_status_label.setStyleSheet("color: green; font-weight: bold;")
            self.scan_btn.setEnabled(True)
        except RuntimeError as e:
            self.hwnd = None
            self.window_status_label.setText(str(e))
            self.window_status_label.setStyleSheet("color: red;")
            self.scan_btn.setEnabled(False)

    def on_scan_toggle(self):
        """开始/停止扫描"""
        if self.worker and self.worker.isRunning():
            # 停止扫描
            self.worker.stop()
            self.scan_btn.setText("停止中...")
            self.scan_btn.setEnabled(False)
        else:
            # 开始扫描
            self._start_scan()

    def _get_main_window(self):
        """获取主窗口实例"""
        widget = self
        while widget.parent():
            widget = widget.parent()
        return widget if widget is not self else None

    def _minimize_main_window(self):
        """最小化主窗口，避免遮挡游戏"""
        main_window = self._get_main_window()
        if main_window:
            main_window.showMinimized()

    def _restore_main_window(self):
        """恢复主窗口"""
        main_window = self._get_main_window()
        if main_window:
            main_window.showNormal()
            main_window.activateWindow()

    def _start_scan(self):
        """启动扫描"""
        if not self.hwnd:
            self.scan_status_label.setText("请先检测游戏窗口")
            return

        scan_mode = "single" if self.scan_mode_combo.currentIndex() == 1 else "global"

        # 全局扫描清空已有结果；单页扫描保留已有结果（累积合并）
        if scan_mode == "global":
            self.scan_results = {}
            self.result_table.setRowCount(0)

        self.scan_btn.setText("停止扫描")
        self.detect_btn.setEnabled(False)
        self.save_btn.setEnabled(False)
        self.scan_mode_combo.setEnabled(False)

        # 最小化主窗口避免遮挡游戏
        self._minimize_main_window()

        self.worker = OCRScanWorker(self.hwnd, scan_mode=scan_mode)
        self.worker.progress.connect(self._on_scan_progress)
        self.worker.finished.connect(self._on_scan_finished)
        self.worker.error.connect(self._on_scan_error)
        self.worker.status.connect(self._on_scan_status)
        self.worker.start()

    def _on_scan_progress(self, progress_dict, primary, secondary):
        """扫描进度回调"""
        # 合并到已有结果中进行实时显示
        merged = dict(self.scan_results)
        merged.update(progress_dict)
        completed = sum(1 for v in merged.values() if v["获取状态"] == "已完成")
        total = len(merged)

        if primary == "单页扫描":
            self.progress_label.setText(
                f"单页扫描中 | 已扫描: {total} 条, 已完成: {completed} 条"
            )
        else:
            self.progress_label.setText(
                f"[{primary}] {secondary} | 已扫描: {total} 条, 已完成: {completed} 条"
            )
        self._update_result_table(merged)

    def _on_scan_finished(self, progress_dict):
        """扫描完成"""
        self._restore_main_window()

        # 合并结果：新结果覆盖旧结果（基于成就 ID）
        self.scan_results.update(progress_dict)
        self._update_result_table(self.scan_results)

        completed = sum(1 for v in self.scan_results.values() if v["获取状态"] == "已完成")
        self.scan_btn.setText("开始扫描")
        self.scan_btn.setEnabled(True)
        self.detect_btn.setEnabled(True)
        self.save_btn.setEnabled(bool(self.scan_results))
        self.scan_mode_combo.setEnabled(True)
        self.scan_status_label.setText("扫描完成")
        self.progress_label.setText(
            f"扫描完成 | 共 {len(self.scan_results)} 条, 已完成 {completed} 条"
        )
        self.worker = None

    def _on_scan_error(self, error_msg):
        """扫描出错"""
        self._restore_main_window()
        self.scan_btn.setText("开始扫描")
        self.scan_btn.setEnabled(True)
        self.detect_btn.setEnabled(True)
        self.scan_mode_combo.setEnabled(True)
        self.scan_status_label.setText(f"扫描出错: {error_msg}")
        self.scan_status_label.setStyleSheet("color: red;")
        self.worker = None

    def _on_scan_status(self, status_text):
        """状态更新"""
        self.scan_status_label.setText(status_text)

    def _update_result_table(self, progress_dict):
        """更新结果预览表格"""
        # 加载成就名称映射（编号 -> 名称）
        if not hasattr(self, '_id_to_name'):
            db = config.load_base_achievements()
            self._id_to_name = {a.get('编号'): a.get('名称', '') for a in db}

        items = list(progress_dict.items())
        self.result_table.setRowCount(len(items))

        for row, (aid, info) in enumerate(items):
            name = self._id_to_name.get(aid, aid)
            status = info.get("获取状态", "未知")

            # 编号
            self.result_table.setItem(row, 0, QTableWidgetItem(aid))

            # 成就名称
            self.result_table.setItem(row, 1, QTableWidgetItem(name))

            # 状态（带颜色）
            status_item = QTableWidgetItem(status)
            if status == "已完成":
                status_item.setForeground(QColor(0, 150, 0))
            elif status == "未完成":
                status_item.setForeground(QColor(200, 150, 0))
            self.result_table.setItem(row, 2, status_item)

            # OCR 原文（与匹配名称不同时高亮）
            ocr_name = info.get("ocr_name", "")
            ocr_item = QTableWidgetItem(ocr_name)
            if ocr_name and ocr_name != name:
                ocr_item.setForeground(QColor(180, 120, 0))
            self.result_table.setItem(row, 3, ocr_item)

    def on_save_results(self):
        """保存扫描结果到用户进度"""
        if not self.scan_results:
            return

        current_user = config.get_current_user()
        existing_progress = config.load_user_progress(current_user)

        updated = 0
        skipped = 0
        for aid, info in self.scan_results.items():
            new_status = info.get("获取状态", "未完成")
            existing_status = existing_progress.get(aid, {}).get("获取状态", "未完成")

            # 禁止降级：已完成 不能变回 未完成
            if existing_status == "已完成" and new_status != "已完成":
                skipped += 1
                continue

            if existing_status != new_status:
                existing_progress[aid] = {"获取状态": new_status}  # 不写入 ocr_name
                updated += 1

        if updated > 0:
            config.save_user_progress(current_user, existing_progress)

        self.scan_status_label.setText(
            f"已保存: 更新 {updated} 条, 跳过 {skipped} 条（防降级）"
        )
        self.scan_status_label.setStyleSheet("color: green; font-weight: bold;")

        # 通知成就管理页刷新
        signal_bus.settings_changed.emit({})

    def apply_theme(self, theme=None):
        """适配明暗主题。"""
        if theme is None:
            theme = config.theme
        from core.styles import BaseStyles, get_scrollbar_style
        self.result_table.setStyleSheet(
            BaseStyles.get_text_input_style(theme) + get_scrollbar_style(theme)
        )
