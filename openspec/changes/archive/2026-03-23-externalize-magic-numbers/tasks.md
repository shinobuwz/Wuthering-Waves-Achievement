## 1. 配置文件与加载函数

- [x] 1.1 创建 `config.ini`（exe 同级目录），包含所有 OCR 参数和游戏检测参数，每项附带中文注释
- [x] 1.2 在 `core/config.py` 中新增 `load_ocr_config()` 函数，使用 configparser 读取 config.ini，返回参数字典，缺失项使用默认值并记录警告日志

## 2. achievement_ocr.py 参数外置

- [x] 2.1 将 `achievement_ocr.py` 顶部的集中管理常量（MATCH_THRESHOLD, NMS_DISTANCE, 名称/状态区域偏移, 滚动参数, 布局百分比参数）改为从 `load_ocr_config()` 获取
- [x] 2.2 将函数体内散落的 `time.sleep()` 延时值改为从配置获取（窗口激活延时、Tab 点击延时、滚动等待延时等）

## 3. game_capture.py 参数外置

- [x] 3.1 将 `game_capture.py` 中的 `GAME_PROCESS_NAMES`、`EXPECTED_WIDTH`、`EXPECTED_HEIGHT` 改为从配置获取

## 4. 打包与验证

- [x] 4.1 更新打包脚本，确保 `config.ini` 被包含在发布包中
- [x] 4.2 验证：config.ini 存在时正常加载，模块导入不报错
