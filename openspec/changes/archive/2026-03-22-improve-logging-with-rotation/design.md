## 上下文

项目当前在 `main.py` 中使用 `logging.basicConfig` 配置了基础日志（仅输出到控制台），其他模块（`config.py`、`crawl_tab.py`、`achievement_table.py`、`avatar_selector.py`）则大量使用 `print()` 打印信息，缺乏统一管理。`achievement_ocr.py` 已正确使用 `logging.getLogger(__name__)`，是参考样板。

日志没有写入文件，也没有轮转机制，无法事后排查问题，且长期运行会丢失历史输出。

## 目标 / 非目标

**目标：**
- 新建 `core/logger.py`，作为全局日志初始化入口
- 日志同时输出到控制台和文件（`logs/ww_achievement.log`）
- 日志文件使用 `RotatingFileHandler` 按大小轮转（5MB × 3备份）
- 将 `main.py` 及各核心模块的 `print()` 替换为 `logger.xxx()`
- 各模块通过 `logging.getLogger(__name__)` 获取 logger，与现有 `achievement_ocr.py` 保持一致

**非目标：**
- 不引入第三方日志库（如 loguru）
- 不修改 `onnxocr/` 下的第三方日志逻辑
- 不修改 `build.py`、`test_scroll.py` 等非核心脚本
- 不做日志级别的 UI 配置界面

## 决策

### 决策1：新建 `core/logger.py` 而不是直接在 `main.py` 扩展

**选择**：独立模块
**理由**：将日志初始化与启动逻辑解耦，便于测试和复用。各模块 `import logger` 无需感知 `main.py`。

**替代方案**：直接在 `main.py` 扩展 `basicConfig` → 耦合度高，且其他模块无法方便地复用同一配置。

### 决策2：使用 `RotatingFileHandler`（按大小）而非 `TimedRotatingFileHandler`（按时间）

**选择**：`RotatingFileHandler`（5MB, backupCount=3）
**理由**：桌面工具使用频率不规律，按时间轮转可能积累大量小文件；按大小更可预测，总占用约 20MB。

**替代方案**：按天轮转 → 不活跃期可能积累许多空/小文件，不适合桌面工具场景。

### 决策3：日志目录使用相对路径 `logs/`

**选择**：运行目录下的 `logs/` 子目录
**理由**：开发时在项目根目录，打包后在 exe 同级目录，符合 Windows 桌面工具的惯例。初始化时自动创建目录。

### 决策4：各模块统一使用 `logging.getLogger(__name__)`

**选择**：标准 `__name__` 命名
**理由**：与已有的 `achievement_ocr.py` 保持一致，logger 名称自动反映模块层级，便于按模块过滤日志。

## 风险 / 权衡

- **[风险] 批量替换 print 可能遗漏或引入回归** → 缓解：逐文件替换，保持日志级别语义一致（INFO/WARNING/ERROR 对应原有 `[INFO]`/`[WARNING]`/`[ERROR]` 前缀）
- **[风险] 日志文件路径在打包后可能写入失败** → 缓解：初始化时捕获异常，若文件 handler 失败则仅保留控制台输出，不影响应用启动
- **[权衡] 磁盘占用** → 5MB × 4（1个当前 + 3个备份）= 最多约 20MB，可接受
