## 为什么

当前项目日志系统存在两个问题：一是大量使用 `print()` 代替 `logger`，导致日志无法集中管理、过滤或持久化；二是没有日志轮转机制，长期运行后日志文件会无限增长，占用磁盘空间。

## 变更内容

- **新增** 统一的日志初始化模块 `core/logger.py`，提供带轮转的 logger 配置
- **新增** 日志轮转：按文件大小轮转（默认 5MB × 3 备份），防止日志无限增长
- **修改** `main.py`：将 `logging.basicConfig` 替换为统一日志初始化，`print()` 改为 `logger`
- **修改** `core/achievement_table.py`：`print()` 改为 `logger`
- **修改** `core/config.py`：`print()` 改为 `logger`
- **修改** `core/crawl_tab.py`：`print()` 改为 `logger`
- **修改** `core/avatar_selector.py`：`print()` 改为 `logger`

## 功能 (Capabilities)

### 新增功能
- `logging-system`: 统一日志系统，包含日志初始化、轮转配置，以及项目各模块 logger 获取规范

### 修改功能
（无规范级行为变更，仅为实现层面的日志输出方式统一）

## 影响

- **新增文件**：`core/logger.py`
- **修改文件**：`main.py`、`core/achievement_table.py`、`core/config.py`、`core/crawl_tab.py`、`core/avatar_selector.py`
- **日志文件路径**：`logs/ww_achievement.log`（运行目录下）
- **依赖**：Python 标准库 `logging.handlers.RotatingFileHandler`，无新增第三方依赖
