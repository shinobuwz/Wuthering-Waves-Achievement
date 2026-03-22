import sys
import logging

from PySide6.QtWidgets import QApplication
from PySide6.QtGui import QFont

from core.logger import setup_logging
from core.styles import get_icon
from version import VERSION

setup_logging()
logger = logging.getLogger(__name__)

def setup_application():
    """设置应用程序基本属性"""
    app = QApplication(sys.argv)
    app.setApplicationName("鸣潮成就管理器")
    app.setApplicationVersion(VERSION)
    app.setOrganizationName("鸣潮成就工具")
    
    # 设置全局默认字体为微软雅黑
    font = QFont("Microsoft YaHei", 9)
    app.setFont(font)
    
    # 设置窗口图标
    icon = get_icon("logo")
    if not icon.isNull():
        app.setWindowIcon(icon)
    
    return app


def main():
    """主函数"""
    try:
        logger.info("正在创建应用实例...")
        # 创建应用实例
        app = setup_application()
        logger.info("应用实例创建完成")

        logger.info("正在导入TemplateMainWindow...")
        from core.main_window import TemplateMainWindow
        logger.info("TemplateMainWindow导入完成")

        logger.info("正在创建主窗口...")
        # 创建主窗口
        main_window = TemplateMainWindow()
        logger.info("主窗口创建完成")

        logger.info("正在显示主窗口...")
        # 直接显示窗口（不使用延迟）
        main_window.show()
        logger.info("主窗口显示完成")

        logger.info("应用启动成功！")
        # 运行应用
        sys.exit(app.exec())
    except Exception as e:
        logger.error("启动应用时发生错误: %s", e, exc_info=True)
        sys.exit(1)


if __name__ == "__main__":
    main()