from PySide6.QtCore import QObject, Signal


class SignalBus(QObject):
    """全局信号总线"""
    
    # 设置相关信号
    settings_changed = Signal(dict)  # 设置变更信号
    user_switched = Signal(str)  # 用户切换信号
    theme_changed = Signal(str)  # 主题切换信号
    category_config_updated = Signal()  # 分类配置更新信号
    
    # 爬虫相关信号
    crawl_started = Signal()  # 开始爬取
    crawl_finished = Signal(object, str)  # 爬取完成 (数据, 消息)
    crawl_progress = Signal(str)  # 爬取进度
    
    # 更新检查相关信号
    update_available = Signal(dict)  # 有可用更新 (更新信息)
    update_check_started = Signal()  # 开始检查更新
    update_check_finished = Signal(dict)  # 检查完成 (结果)
    
    # 通用消息信号
    log_message = Signal(str, str, dict)  # 日志消息 (级别, 消息, 额外数据)
    
    def __init__(self):
        super().__init__()


# 创建全局信号实例
signal_bus = SignalBus()