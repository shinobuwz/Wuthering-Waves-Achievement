## 为什么

当前鸣潮成就管理器依赖用户手动勾选成就完成状态，或通过 API 爬取数据（需要认证信息）。这两种方式都有明显痛点：手动勾选效率低且容易遗漏，API 爬取需要用户获取 devcode 和 token，门槛较高。通过 OCR 直接识别游戏内成就界面，可以提供一种零门槛、自动化的成就状态同步方式。

## 变更内容

- **新增 OCR 扫描功能**：集成本地 ONNX OCR 引擎，自动检测游戏窗口并截图识别成就完成状态
- **新增 OCR 扫描 Tab 页**：在现有应用中添加新的标签页，提供扫描控制、进度显示和结果预览
- **集成 onnxocr 模块**：将 `D:\github\onnxocr` 拷贝到项目目录作为内部依赖
- **新增依赖**：opencv-python、mss、pywin32（或 ctypes）用于截图和窗口操作

## 功能 (Capabilities)

### 新增功能

- `game-window-capture`: 游戏窗口检测与截图——通过 Win32 API 定位鸣潮窗口，使用 mss 进行前台截图，固定适配 1920x1080 分辨率
- `achievement-ocr-recognition`: 成就 OCR 识别与匹配——裁剪成就列表区域，按固定高度分割条目，OCR 识别成就名称，通过模糊匹配关联到 base_achievements.json，判断完成状态（"进行中" vs 日期）
- `ocr-auto-scroll`: 自动滚动扫描——模拟鼠标滚轮自动翻页右侧成就列表，通过成就名称去重检测是否到底，循环直到扫描完当前子分类的所有成就
- `ocr-scan-tab`: OCR 扫描界面——新增 PySide6 Tab 页，提供窗口检测、开始/停止扫描、实时进度、扫描结果预览，结果自动写入 user_progress

### 修改功能

（无现有功能需求变更）

## 影响

- **新增文件**：`core/ocr_tab.py`、`core/game_capture.py`、`core/achievement_ocr.py`
- **新增目录**：`onnxocr/`（含预训练模型约 50MB+）
- **修改文件**：`core/main_window.py`（注册新 Tab）、`requirements.txt`（新增依赖）
- **新增依赖**：opencv-python、mss、onnxruntime、numpy、Pillow、shapely、pyclipper
- **数据写入**：通过现有 `config.save_user_progress()` 更新用户成就状态，无需改动数据结构
