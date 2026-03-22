## 1. 新建日志初始化模块

- [x] 1.1 创建 `core/logger.py`，实现 `setup_logging()` 函数：配置根 logger 为 INFO 级别，添加控制台 StreamHandler 和 RotatingFileHandler（5MB × 3备份，输出到 `logs/ww_achievement.log`）
- [x] 1.2 在 `setup_logging()` 中自动创建 `logs/` 目录（若不存在），并在文件 handler 初始化失败时捕获异常、降级为仅控制台输出

## 2. 修改 main.py

- [x] 2.1 移除 `logging.basicConfig(...)` 调用，改为调用 `from core.logger import setup_logging; setup_logging()`
- [x] 2.2 在 `main.py` 顶部添加 `logger = logging.getLogger(__name__)`，将函数内所有 `print()` 替换为对应级别的 `logger` 调用

## 3. 替换 core/config.py 中的 print

- [x] 3.1 在文件顶部添加 `import logging; logger = logging.getLogger(__name__)`
- [x] 3.2 将所有 `print(f"[INFO] ...")` → `logger.info(...)`，`print(f"[ERROR] ...")` → `logger.error(...)`，`print(f"[DEBUG] ...")` → `logger.debug(...)`，`print(f"[SUCCESS] ...")` → `logger.info(...)`

## 4. 替换 core/crawl_tab.py 中的 print

- [x] 4.1 在文件顶部添加 `import logging; logger = logging.getLogger(__name__)`
- [x] 4.2 将所有 `print(f"[INFO] ...")` / `print(f"[WARNING] ...")` / `print(f"[ERROR] ...")` / `print(f"[SUCCESS] ...")` 替换为对应 `logger` 级别调用

## 5. 替换 core/achievement_table.py 中的 print

- [x] 5.1 在文件顶部添加 `import logging; logger = logging.getLogger(__name__)`
- [x] 5.2 将所有 `print(f"[INFO] ...")` / `print(f"[ERROR] ...")` / `print(f"[DEBUG] ...")` 替换为对应 `logger` 级别调用

## 6. 替换 core/avatar_selector.py 中的 print

- [x] 6.1 在文件顶部添加 `import logging; logger = logging.getLogger(__name__)`
- [x] 6.2 将 `print(f"警告：...")` 替换为 `logger.warning(...)`
