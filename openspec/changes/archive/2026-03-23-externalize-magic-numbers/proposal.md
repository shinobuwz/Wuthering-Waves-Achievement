## 为什么

OCR 扫描参数（模板匹配阈值、像素偏移、滚动参数、界面布局百分比、延时等）全部硬编码在 `achievement_ocr.py` 顶部和函数体内。打包成 exe 后用户无法调整这些参数来适配不同分辨率或游戏更新后的界面变化，只能等待开发者发布新版本。将这些 magic number 外置到 `config.ini` 可让用户自行微调，也方便开发阶段快速迭代。

## 变更内容

- **新增**：`resources/config.ini` 配置文件，包含所有 OCR 扫描相关的可调参数
- **新增**：配置加载模块，启动时显式读取 `config.ini`，缺失时使用默认值
- **修改**：`core/achievement_ocr.py` 中的常量改为从配置加载，不再硬编码
- **修改**：`core/game_capture.py` 中的游戏进程名和期望分辨率改为从配置加载

## 功能 (Capabilities)

### 新增功能
- `external-config`: 外置配置文件加载机制，包括 config.ini 的定义、加载、默认值回退

### 修改功能
（无规范级行为变更，仅实现层面的参数来源变更）

## 影响

- `resources/config.ini`：新增文件
- `core/config.py`：新增 ini 配置加载函数
- `core/achievement_ocr.py`：常量从硬编码改为配置加载
- `core/game_capture.py`：进程名和分辨率从硬编码改为配置加载
- 打包脚本：需确保 `config.ini` 被包含在发布包中
