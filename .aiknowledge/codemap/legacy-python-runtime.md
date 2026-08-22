# 旧版 Python 运行链路

## Purpose

导航旧版 Python/PySide6 应用的启动、配置、成就管理、Wiki 获取和完整 OCR 扫描边界。该实现仍可独立运行，并在原生版全局扫描能力完成前保留最完整的分类导航。

## Entry points

- `main.py`：创建 QApplication、设置全局样式并启动 `core.main_window.TemplateMainWindow`。
- `core/main_window.py`：旧版主窗口与页面组合；继续读取 `core/config.py` 的全局配置。
- `core/config.py`：资源路径、`resources/config.json`、基础成就库、分类配置和按 UID 的用户进度读写。
- `core/crawl_tab.py`：调用 Kuro Wiki 接口、缓存响应并解析成就 HTML/分类。
- `core/ocr_tab.py`：OCR worker、单页/全局模式、结果预览和保存时防降级。
- Runtime：`python main.py`；真实 OCR 辅助入口为 `test_scroll.py` 和 `test_tab_switch.py`。

## Boundaries

- 旧版可变文件位于 `resources/`：配置、基础库、缓存和 `user_progress_{uid}.json` 的职责不同，不要混写。
- `core/achievement_ocr.py` 负责模板匹配、相对区域裁剪、onnxocr 识别、名称模糊匹配、状态解析、滚轮和分类遍历；`core/game_capture.py` 负责 Windows 进程/窗口发现与截图。
- OCR 结果先在 `core/ocr_tab.py` 中展示，用户保存时才写用户进度；该流程不提供原生版 generation 或跨实现同步。
- `onnxocr/` 是本地 PP-OCR Python 运行时和模型资源，不是原生版 C++ ABI 的替代持久化层。

## Read next

- 先读 `core/config.py` 的 `get_resource_path`、用户进度和分类配置方法。
- 需要 OCR 细节时读 `core/achievement_ocr.py`、`core/game_capture.py` 和 `resources/config.ini`。
- 需要 Wiki 解析时读 `core/crawl_tab.py` 的缓存元数据与 HTML parsing，再对照 `resources/base_achievements.json`。
- 需要界面行为时读 `core/manage_tab.py`、`core/ocr_tab.py` 和 `core/signal_bus.py`。
- `verified_against: commit:94aeb30`
