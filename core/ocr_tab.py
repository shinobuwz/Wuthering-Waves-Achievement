"""
OCR 扫描标签页
提供游戏窗口检测、OCR 成就扫描、结果预览和进度更新功能
"""
from PySide6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                               QPushButton, QTableWidget, QTableWidgetItem,
                               QGroupBox, QProgressBar)
from PySide6.QtCore import Qt, QThread, Signal
from PySide6.QtGui import QColor

from core.config import config
from core.signal_bus import signal_bus
from core.styles import get_button_style, get_font_gray_style, ColorPalette


class OCRScanWorker(QThread):
    """OCR 扫描工作线程"""
    progress = Signal(list, int)  # (当前结果列表, 轮次)
    finished = Signal(list)       # 最终结果列表
    error = Signal(str)           # 错误信息
    status = Signal(str)          # 状态文字

    def __init__(self, hwnd):
        super().__init__()
        self.hwnd = hwnd
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

            self.status.emit("开始扫描...")
            from core.achievement_ocr import scan_with_scroll

            def on_progress(results, round_num):
                self.progress.emit(results, round_num)
                self.status.emit(f"第 {round_num} 轮扫描，已识别 {len(results)} 条成就")

            results = scan_with_scroll(
                self.hwnd,
                ocr_model,
                achievements_db,
                callback=on_progress,
                stop_flag=lambda: self._stop,
            )

            self.finished.emit(results)

        except Exception as e:
            self.error.emit(str(e))


class OCRScanTab(QWidget):
    """OCR 扫描标签页"""

    def __init__(self):
        super().__init__()
        self.hwnd = None
        self.worker = None
        self.scan_results = []
        self.init_ui()

    def init_ui(self):
        layout = QVBoxLayout(self)
        layout.setSpacing(10)

        # === 窗口检测区域 ===
        detect_group = QGroupBox("游戏窗口")
        detect_layout = QHBoxLayout(detect_group)

        self.detect_btn = QPushButton("检测游戏窗口")
        self.detect_btn.clicked.connect(self.on_detect_window)
        detect_layout.addWidget(self.detect_btn)

        self.window_status_label = QLabel("未检测")
        detect_layout.addWidget(self.window_status_label, 1)

        layout.addWidget(detect_group)

        # === 扫描控制区域 ===
        scan_group = QGroupBox("扫描控制")
        scan_layout = QHBoxLayout(scan_group)

        self.scan_btn = QPushButton("开始扫描")
        self.scan_btn.clicked.connect(self.on_scan_toggle)
        self.scan_btn.setEnabled(False)
        scan_layout.addWidget(self.scan_btn)

        self.scan_status_label = QLabel("就绪")
        scan_layout.addWidget(self.scan_status_label, 1)

        layout.addWidget(scan_group)

        # === 进度区域 ===
        progress_group = QGroupBox("扫描进度")
        progress_layout = QVBoxLayout(progress_group)

        self.progress_label = QLabel("已识别: 0 条成就")
        progress_layout.addWidget(self.progress_label)

        layout.addWidget(progress_group)

        # === 结果预览表格 ===
        result_group = QGroupBox("扫描结果")
        result_layout = QVBoxLayout(result_group)

        # 操作按钮行
        btn_layout = QHBoxLayout()
        self.save_btn = QPushButton("保存到用户进度")
        self.save_btn.clicked.connect(self.on_save_results)
        self.save_btn.setEnabled(False)
        btn_layout.addWidget(self.save_btn)
        btn_layout.addStretch()
        result_layout.addLayout(btn_layout)

        self.result_table = QTableWidget()
        self.result_table.setColumnCount(4)
        self.result_table.setHorizontalHeaderLabels(
            ["OCR 识别名称", "匹配结果", "状态", "置信度"]
        )
        self.result_table.horizontalHeader().setStretchLastSection(True)
        self.result_table.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        self.result_table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        # 设置列宽
        self.result_table.setColumnWidth(0, 250)
        self.result_table.setColumnWidth(1, 250)
        self.result_table.setColumnWidth(2, 100)
        self.result_table.setColumnWidth(3, 80)

        result_layout.addWidget(self.result_table)

        layout.addWidget(result_group, 1)  # 表格区域占剩余空间

        # 应用样式
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

        self.scan_results = []
        self.result_table.setRowCount(0)
        self.scan_btn.setText("停止扫描")
        self.detect_btn.setEnabled(False)
        self.save_btn.setEnabled(False)

        # 最小化主窗口避免遮挡游戏
        self._minimize_main_window()

        self.worker = OCRScanWorker(self.hwnd)
        self.worker.progress.connect(self._on_scan_progress)
        self.worker.finished.connect(self._on_scan_finished)
        self.worker.error.connect(self._on_scan_error)
        self.worker.status.connect(self._on_scan_status)
        self.worker.start()

    def _on_scan_progress(self, results, round_num):
        """扫描进度回调"""
        matched = sum(1 for r in results if r["编号"])
        unmatched = sum(1 for r in results if not r["编号"])
        self.progress_label.setText(
            f"第 {round_num} 轮 | 已识别: {matched} 条匹配, {unmatched} 条未匹配"
        )
        self._update_result_table(results)

    def _on_scan_finished(self, results):
        """扫描完成"""
        self._restore_main_window()
        self.scan_results = results
        self._update_result_table(results)

        matched = sum(1 for r in results if r["编号"])
        self.scan_btn.setText("开始扫描")
        self.scan_btn.setEnabled(True)
        self.detect_btn.setEnabled(True)
        self.save_btn.setEnabled(bool(results))
        self.scan_status_label.setText("扫描完成")
        self.progress_label.setText(
            f"扫描完成 | 共识别 {len(results)} 条, 匹配 {matched} 条"
        )
        self.worker = None

    def _on_scan_error(self, error_msg):
        """扫描出错"""
        self._restore_main_window()
        self.scan_btn.setText("开始扫描")
        self.scan_btn.setEnabled(True)
        self.detect_btn.setEnabled(True)
        self.scan_status_label.setText(f"扫描出错: {error_msg}")
        self.scan_status_label.setStyleSheet("color: red;")
        self.worker = None

    def _on_scan_status(self, status_text):
        """状态更新"""
        self.scan_status_label.setText(status_text)

    def _update_result_table(self, results):
        """更新结果预览表格"""
        self.result_table.setRowCount(len(results))

        for row, result in enumerate(results):
            # OCR 名称
            ocr_item = QTableWidgetItem(result.get("ocr_name", ""))
            self.result_table.setItem(row, 0, ocr_item)

            # 匹配结果
            matched_name = result.get("matched_name", "")
            match_item = QTableWidgetItem(matched_name or "未匹配")
            if not matched_name:
                match_item.setForeground(QColor(255, 100, 100))
                match_item.setBackground(QColor(255, 230, 230))
            self.result_table.setItem(row, 1, match_item)

            # 状态
            status = result.get("状态", "未知")
            status_item = QTableWidgetItem(status)
            if status == "已完成":
                status_item.setForeground(QColor(0, 150, 0))
            elif status == "未完成":
                status_item.setForeground(QColor(200, 150, 0))
            self.result_table.setItem(row, 2, status_item)

            # 置信度
            confidence = result.get("置信度", 0)
            conf_item = QTableWidgetItem(f"{confidence:.0%}" if confidence else "-")
            self.result_table.setItem(row, 3, conf_item)

    def on_save_results(self):
        """保存扫描结果到用户进度"""
        if not self.scan_results:
            return

        current_user = config.get_current_user()
        progress = config.load_user_progress(current_user)

        updated = 0
        skipped = 0
        for result in self.scan_results:
            achievement_id = result.get("编号")
            status = result.get("状态")
            if not achievement_id or status == "未知":
                continue

            existing = progress.get(achievement_id, {})
            existing_status = existing.get("获取状态", "未完成")

            # 禁止降级：已完成 不能变回 未完成
            if existing_status == "已完成" and status == "未完成":
                skipped += 1
                continue

            if existing_status != status:
                progress[achievement_id] = {"获取状态": status}
                updated += 1

        if updated > 0:
            config.save_user_progress(current_user, progress)

        self.scan_status_label.setText(
            f"已保存: 更新 {updated} 条, 跳过 {skipped} 条（防降级）"
        )
        self.scan_status_label.setStyleSheet("color: green; font-weight: bold;")

        # 通知成就管理页刷新
        signal_bus.settings_changed.emit({})

    def apply_theme(self, theme=None):
        """适配明暗主题"""
        if theme is None:
            theme = config.theme

        colors = ColorPalette.Dark if theme == "dark" else ColorPalette.Light

        # 按钮样式
        btn_style = get_button_style(theme)
        self.detect_btn.setStyleSheet(btn_style)
        self.scan_btn.setStyleSheet(btn_style)
        self.save_btn.setStyleSheet(btn_style)

        # 标签颜色
        label_style = f"color: {colors.TEXT_PRIMARY};"
        self.window_status_label.setStyleSheet(label_style)
        self.scan_status_label.setStyleSheet(label_style)
        self.progress_label.setStyleSheet(label_style)

        # GroupBox 样式
        group_style = f"""
            QGroupBox {{
                color: {colors.TEXT_PRIMARY};
                font-weight: bold;
                border: 1px solid {colors.TEXT_SECONDARY if hasattr(colors, 'TEXT_SECONDARY') else '#ccc'};
                border-radius: 5px;
                margin-top: 10px;
                padding-top: 10px;
            }}
            QGroupBox::title {{
                subcontrol-origin: margin;
                left: 10px;
                padding: 0 5px;
            }}
        """
        for group in self.findChildren(QGroupBox):
            group.setStyleSheet(group_style)
