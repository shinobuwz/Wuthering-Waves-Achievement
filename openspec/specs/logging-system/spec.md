## 新增需求

### 需求:统一日志初始化模块
系统必须提供 `core/logger.py` 模块，该模块负责全局日志系统的初始化配置，包括控制台和文件两个输出 handler，其他模块禁止直接调用 `logging.basicConfig`。

#### 场景:应用启动时初始化日志
- **当** `main.py` 在启动时调用 `core.logger.setup_logging()`
- **那么** 根 logger 被配置为 INFO 级别，同时挂载控制台 handler 和文件 handler

#### 场景:各模块获取 logger
- **当** 任意模块执行 `logger = logging.getLogger(__name__)`
- **那么** 该 logger 继承根 logger 的配置，日志输出同时出现在控制台和日志文件中

### 需求:日志文件轮转
系统必须使用 `RotatingFileHandler` 将日志写入文件，文件大小上限为 5MB，最多保留 3 个备份文件，日志目录为运行目录下的 `logs/` 子目录。

#### 场景:日志文件达到上限后自动轮转
- **当** 当前日志文件 `logs/ww_achievement.log` 大小达到 5MB
- **那么** 系统自动将其重命名为 `.log.1`，并创建新的 `ww_achievement.log` 继续写入，旧备份依次顺延，超出 3 个的最旧备份被删除

#### 场景:日志目录不存在时自动创建
- **当** 应用首次启动且 `logs/` 目录不存在
- **那么** 系统自动创建该目录后再初始化文件 handler，不抛出异常

#### 场景:文件 handler 初始化失败时降级
- **当** 由于权限或其他原因无法创建日志文件
- **那么** 系统仅保留控制台 handler，记录一条警告后正常启动，不影响应用功能

### 需求:模块日志替换
项目核心模块（`main.py`、`core/config.py`、`core/crawl_tab.py`、`core/achievement_table.py`、`core/avatar_selector.py`）中所有 `print()` 日志输出必须替换为对应级别的 `logger` 调用，日志级别须与原 `[INFO]`/`[WARNING]`/`[ERROR]`/`[DEBUG]`/`[SUCCESS]` 前缀语义保持一致。

#### 场景:INFO 级别替换
- **当** 原代码使用 `print(f"[INFO] ...")` 或无前缀的进度信息 `print("...")`
- **那么** 替换为 `logger.info("...")`，日志内容中不再包含 `[INFO]` 前缀

#### 场景:WARNING 级别替换
- **当** 原代码使用 `print(f"[WARNING] ...")` 或 `print(f"警告：...")`
- **那么** 替换为 `logger.warning("...")`

#### 场景:ERROR 级别替换
- **当** 原代码使用 `print(f"[ERROR] ...")`
- **那么** 替换为 `logger.error("...")`

#### 场景:DEBUG 级别替换
- **当** 原代码使用 `print(f"[DEBUG] ...")`
- **那么** 替换为 `logger.debug("...")`

#### 场景:SUCCESS 视为 INFO
- **当** 原代码使用 `print(f"[SUCCESS] ...")`
- **那么** 替换为 `logger.info("...")`（Python 标准日志无 SUCCESS 级别）
