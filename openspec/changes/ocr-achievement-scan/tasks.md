## 1. 环境准备与依赖集成

- [x] 1.1 将 onnxocr 目录拷贝到项目根目录，验证 `from onnxocr import ONNXPaddleOcr` 可正常导入
- [x] 1.2 更新 requirements.txt，添加 opencv-python、mss、onnxruntime、numpy、Pillow、shapely、pyclipper 依赖
- [x] 1.3 安装新增依赖并验证导入无误

## 2. 游戏窗口检测与截图 (game_capture.py)

- [x] 2.1 创建 core/game_capture.py，实现 find_game_window() 函数：通过 ctypes FindWindow 按窗口标题查找鸣潮窗口，返回 HWND
- [x] 2.2 实现窗口标题回退匹配：FindWindow 精确匹配失败时，枚举所有窗口进行模糊匹配
- [x] 2.3 实现 get_window_rect(hwnd) 函数：通过 GetWindowRect 获取窗口位置和大小，校验是否为 1920x1080
- [x] 2.4 实现 capture_window(hwnd) 函数：使用 mss 截取窗口所在屏幕区域，返回 numpy array (BGR)

## 3. 成就 OCR 识别与匹配 (achievement_ocr.py)

- [x] 3.1 创建 core/achievement_ocr.py，定义成就区域的像素坐标常量（右侧列表区域、条目高度、名称区域、状态区域）
- [x] 3.2 实现 crop_achievement_list(screenshot) 函数：从完整截图裁剪右侧成就列表区域
- [x] 3.3 实现 split_achievement_items(list_image) 函数：按固定高度将列表切割为单条成就图像列表
- [x] 3.4 实现 preprocess_image(item_image) 函数：对比度增强和二值化预处理
- [x] 3.5 实现 recognize_achievement_name(item_image, ocr_model) 函数：裁剪名称区域并 OCR 识别
- [x] 3.6 实现 recognize_achievement_status(item_image, ocr_model) 函数：裁剪状态区域，OCR 识别"进行中"或日期
- [x] 3.7 实现 match_achievement(ocr_name, achievements_db) 函数：使用 difflib.SequenceMatcher 模糊匹配，阈值 0.7，返回最佳匹配编号和置信度
- [x] 3.8 实现 scan_single_page(screenshot, ocr_model, achievements_db) 函数：整合上述步骤，返回当前页面的 [{编号, 名称, 状态, 置信度}] 列表

## 4. 自动滚动扫描 (achievement_ocr.py)

- [x] 4.1 实现 simulate_scroll(hwnd, scroll_amount) 函数：将鼠标移到右侧成就列表区域中心，模拟鼠标滚轮向下滚动
- [x] 4.2 实现 scan_with_scroll(hwnd, ocr_model, achievements_db, callback) 函数：循环执行截图→OCR→滚动，通过成就名称集合对比检测到底，支持回调函数报告进度
- [x] 4.3 实现去重逻辑：基于匹配到的成就编号去重，确保每条成就只记录一次
- [x] 4.4 实现停止机制：通过外部标志位支持中途停止扫描

## 5. OCR 扫描 Tab 页面 (ocr_tab.py)

- [x] 5.1 创建 core/ocr_tab.py，定义 OCRScanTab 类继承 QWidget，搭建基本布局
- [x] 5.2 实现窗口检测区域：添加"检测游戏窗口"按钮和状态标签
- [x] 5.3 实现扫描控制区域：添加"开始扫描"/"停止扫描"按钮，未检测窗口时禁用扫描按钮
- [x] 5.4 实现进度显示区域：显示已识别成就数量、当前状态文字
- [x] 5.5 实现结果预览表格：展示成就名称、匹配结果、完成状态，未匹配项醒目标记
- [x] 5.6 实现扫描工作线程：使用 QThread 在后台执行 OCR 扫描，避免阻塞 UI
- [x] 5.7 实现结果写入：扫描完成后通过 config.save_user_progress() 更新用户进度，禁止将"已完成"降级为"未完成"
- [x] 5.8 实现 apply_theme() 方法适配明暗主题

## 6. 应用集成

- [x] 6.1 在 core/main_window.py 中导入并注册 OCRScanTab，添加到 tab_widget
- [x] 6.2 在 main_window.py 的 apply_theme() 中添加 OCR Tab 的主题切换支持
- [x] 6.3 端到端测试：启动应用 → 切换到 OCR 扫描 Tab → 检测窗口 → 开始扫描 → 验证结果写入
