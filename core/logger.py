import logging
import logging.handlers
import os
import sys
from pathlib import Path


def setup_logging():
    """初始化全局日志系统，输出到控制台和轮转文件。"""
    root_logger = logging.getLogger()
    root_logger.setLevel(logging.INFO)

    formatter = logging.Formatter(
        "%(asctime)s [%(name)s] %(levelname)s: %(message)s",
        datefmt="%H:%M:%S",
    )

    # 控制台 handler
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setFormatter(formatter)
    root_logger.addHandler(console_handler)

    # 文件 handler（带轮转）
    try:
        log_dir = Path(sys.argv[0]).parent / "logs"
        log_dir.mkdir(parents=True, exist_ok=True)
        log_file = log_dir / "ww_achievement.log"

        file_handler = logging.handlers.RotatingFileHandler(
            log_file,
            maxBytes=5 * 1024 * 1024,  # 5MB
            backupCount=3,
            encoding="utf-8",
        )
        file_handler.setFormatter(formatter)
        root_logger.addHandler(file_handler)
    except Exception as e:
        root_logger.warning("无法初始化日志文件 handler，仅使用控制台输出：%s", e)
