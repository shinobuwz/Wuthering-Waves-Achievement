## 新增需求

### 需求:外置配置文件定义
系统必须提供 `resources/config.ini` 文件，以 INI 格式存储所有 OCR 扫描相关的可调参数，每个参数必须附带中文注释说明用途。

#### 场景:配置文件结构
- **当** 查看 config.ini 文件内容
- **那么** 文件必须包含以下分节：`[ocr.matching]`、`[ocr.name_region]`、`[ocr.status_region]`、`[ocr.scroll]`、`[ocr.layout]`、`[ocr.timing]`、`[game]`，每个参数必须有注释说明

#### 场景:参数完整性
- **当** 对比 config.ini 与代码中原有的硬编码常量
- **那么** config.ini 必须包含 `achievement_ocr.py` 中所有集中管理的常量（MATCH_THRESHOLD, NMS_DISTANCE, NAME_DX/DY/W/H, STATUS_DX/DY/W/H, SCROLL_LENGTH/TIMES/TIMES_TAB/DELAY, 所有 PCT 布局参数）以及 `game_capture.py` 中的进程名和期望分辨率

### 需求:配置加载与默认值回退
系统必须在启动时加载 config.ini，任何配置项缺失或解析失败时必须使用硬编码默认值。

#### 场景:正常加载
- **当** config.ini 存在且格式正确
- **那么** 系统必须读取其中的参数值并应用到 OCR 扫描流程

#### 场景:配置文件缺失
- **当** config.ini 文件不存在
- **那么** 系统必须使用所有参数的硬编码默认值正常运行，并记录警告日志

#### 场景:单项配置解析失败
- **当** config.ini 中某个参数值格式错误（如非数字字符串）
- **那么** 系统必须对该参数使用默认值，记录警告日志，不影响其他参数的加载

#### 场景:配置文件路径
- **当** 系统查找 config.ini
- **那么** 系统必须通过 `get_resource_path("resources/config.ini")` 定位文件，与其他资源文件路径策略一致
