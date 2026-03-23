## 上下文

当前所有 OCR 扫描参数硬编码在 `core/achievement_ocr.py` 顶部（约 20 个常量）和函数体内的 `time.sleep()` 调用中。`core/game_capture.py` 也有硬编码的进程名和期望分辨率。用户打包后无法修改这些值。

项目已有 `core/config.py` 的 `Config` 类管理 JSON 配置和用户进度，但不涉及 OCR 参数。

## 目标 / 非目标

**目标：**
- 创建 `resources/config.ini`，包含所有可调参数及中文注释
- 使用 Python 标准库 `configparser` 加载，无新依赖
- 配置文件缺失或某项缺失时优雅回退到默认值
- 打包后用户可直接编辑 `config.ini` 调整参数

**非目标：**
- 不提供 GUI 编辑配置的界面
- 不支持运行时热重载（需重启应用）
- 不外置 UI 样式参数（列宽、动画等保持硬编码）

## 决策

### 决策 1：使用 configparser + INI 格式

选择 Python 内置 `configparser` 读取 INI 格式文件。

**替代方案：**
- TOML（`tomllib`）：Python 3.11+ 才内置，3.8 需额外依赖
- YAML：需 PyYAML 依赖
- 扩展现有 JSON 配置：JSON 不支持注释，用户编辑体验差

**理由：** INI 格式用户友好（支持注释、分节），configparser 零依赖，适合简单键值配置。

### 决策 2：在 config.py 中新增 `load_ocr_config()` 函数

在现有 `core/config.py` 中新增函数，返回一个包含所有参数的字典。`achievement_ocr.py` 和 `game_capture.py` 在模块加载时调用该函数获取配置值。

**替代方案：**
- 新建独立的 `ocr_config.py` 模块：增加文件数量，与现有 config 体系分离
- 每个模块自行读取 INI：重复代码，路径管理分散

**理由：** 复用现有 `config.py` 的 `get_resource_path()`，集中配置加载逻辑。

### 决策 3：配置分节结构

```ini
[ocr.matching]        # 模板匹配与模糊匹配
[ocr.name_region]     # 名称裁剪区域
[ocr.status_region]   # 状态裁剪区域
[ocr.scroll]          # 滚动参数
[ocr.layout]          # 界面布局百分比
[ocr.timing]          # 各种延时
[game]                # 游戏检测参数
```

**替代方案：**
- 扁平结构（所有 key 在一个 section）：参数多了不好找
- 按文件名分节：语义不清晰

**理由：** 按功能领域分节，用户能快速定位需要调整的参数。

## 风险 / 权衡

- [INI 不支持列表类型] → `PRIMARY_TAB_ICON_Y_PCTS` 等列表参数使用逗号分隔字符串，加载时 split + float 转换
- [配置文件被用户误编辑损坏] → 每个参数都有硬编码默认值作为 fallback，单项解析失败时记录警告日志并使用默认值
- [打包后 config.ini 路径] → 使用现有 `get_resource_path()` 定位，与其他资源文件一致
