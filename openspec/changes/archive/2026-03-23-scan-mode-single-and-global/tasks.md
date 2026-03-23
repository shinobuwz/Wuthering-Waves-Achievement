## 1. UI 层：扫描模式选择控件

- [x] 1.1 在 `core/ocr_tab.py` 的扫描控制区域添加 QComboBox，选项为"单页扫描"和"全局扫描"（默认选中"全局扫描"），放置在"开始扫描"按钮左侧
- [x] 1.2 扫描开始时禁用 QComboBox，扫描结束（完成或停止）时重新启用

## 2. 扫描逻辑分发

- [x] 2.1 在 `core/achievement_ocr.py` 中新增 `scan_current_page(hwnd, ocr_model, db, stop_event, progress_callback)` 函数，内部调用 `scan_with_scroll()` 扫描当前可见页面，不执行任何 Tab 切换
- [x] 2.2 修改 `core/ocr_tab.py` 的 `OCRScanWorker`，根据当前选中的扫描模式分发调用：单页模式调用 `scan_current_page()`，全局模式调用 `scan_all_tabs()`

## 3. 结果累积策略

- [x] 3.1 修改 `OCRScanWorker` 和结果处理逻辑：全局扫描开始时清空已有结果；单页扫描开始时保留已有结果，将新结果按成就 ID 合并（相同 ID 覆盖旧条目）
- [x] 3.2 更新结果预览表格的刷新逻辑，支持合并后的增量更新

## 4. 进度显示适配

- [x] 4.1 修改进度回调和进度标签显示逻辑：全局模式显示"[一级分类] 二级分类 | 已扫描: X 条, 已完成: Y 条"；单页模式显示"单页扫描中 | 已扫描: X 条, 已完成: Y 条"

## 5. 规范归档

- [x] 5.1 将变更规范归档到项目 specs 目录
