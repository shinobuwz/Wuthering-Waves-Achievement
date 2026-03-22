import json
import sys
import os
import logging
from pathlib import Path

logger = logging.getLogger(__name__)


def setup_resources_structure():
    """检查并创建resources文件夹结构"""
    # # 创建resources目录（如果不存在）
    resources_dir = get_resource_path("resources")
    if not resources_dir.exists():
        logger.info("创建resources目录: %s", resources_dir)
        resources_dir.mkdir(exist_ok=True)

    # 定义子文件夹和对应的说明内容
    subdirs = {
        "characters": "角色肖像图文件夹\n\n此文件夹用于存放游戏角色的全身肖像图文件。\n\n获取方式：\n1. 访问库街区Wiki-角色列表页面\n   链接：https://wiki.kurobbs.com/mc/catalogue/list?fid=1099&sid=1105\n2. 点击每个角色进入详情页\n3. 拖动角色的全身肖像图到此文件夹\n4. 将图片重命名为角色名(如：今汐.webp)\n\n支持的格式：.webp\n\n重要提示：\n• 肖像图文件名必须与profile文件夹中的头像文件名完全一致\n• 这样切换头像时才能自动联动显示对应的肖像图\n\n请勿删除此文件夹。",
        "img": "界面背景图片文件夹\n\n此文件夹用于存放应用程序的背景图片文件。\n\n默认背景图片：\n• background-light.png - 浅色主题背景\n• background-dark.png - 深色主题背景\n\n支持的格式：.png, .jpg\n\n使用说明：\n• 替换这些文件可以自定义应用背景\n• 文件名必须保持一致才能被正确识别\n\n请勿删除此文件夹。",
        "profile": "用户头像文件夹\n\n此文件夹用于存放用户选择的头像图片文件。\n\n获取方式：\n1. 访问库街区Wiki-角色头像页面\n   链接：https://wiki.kurobbs.com/mc/catalogue/list?fid=1099&sid=1363\n2. 直接拖动每个角色的头像图片到此文件夹\n3. 将图片重命名为角色名(如：今汐.png)\n\n支持的格式：.png\n\n重要提示：\n• 头像文件名必须与characters文件夹中的肖像图文件名完全一致\n• 在主窗口点击头像切换头像，会自动更新同角色肖像图\n\n请勿删除此文件夹。"
    }

    # 检查并创建子文件夹和说明文件
    for subdir_name, description in subdirs.items():
        subdir_path = resources_dir / subdir_name

        # 创建子文件夹（如果不存在）
        if not subdir_path.exists():
            logger.info("创建子目录: %s", subdir_path)
            subdir_path.mkdir(exist_ok=True)

        # 创建说明文件（如果不存在）
        readme_file = subdir_path / "文件夹说明.txt"
        if not readme_file.exists():
            logger.info("创建说明文件: %s", readme_file)
            with open(readme_file, 'w', encoding='utf-8') as f:
                f.write(description)

    logger.info("Resources文件夹结构检查完成")


def get_resource_path(relative_path):
    """获取资源文件的绝对路径，支持开发环境和打包后的环境"""
    base_path = Path(sys.argv[0]).parent

    return base_path / relative_path


setup_resources_structure()


class Config:
    """配置管理类"""

    def __init__(self):
        self.config_file = get_resource_path("resources/config.json")

        # 直接定义属性，与模板保持一致
        self.current_user = ""
        self.users = {}
        self.devcode = ""
        self.token = ""
        self.theme = "light"
        self.auto_save = True
        self.use_background = True
        self.custom_background_light = ""
        self.custom_background_dark = ""
        self.current_profile = ""
        self.user_avatars = {}  # 存储用户头像路径
        self.user_character_names = {}  # 存储用户选择的角色名
        self.crawl_settings = {
            "default_output_file": "鸣潮成就数据.json",
            "default_version_filter": "",
            "save_logs": True
        }

        # GitHub仓库配置（用于更新检查）
        self.github_owner = "sliencezhang"  # GitHub用户名
        self.github_repo = "Wuthering-Waves-Achievement"  # 仓库名

        # 更新下载链接配置
        self.update_download_url = "https://wwbml.lanzoum.com/b01880knob"  # 蓝奏云下载链接
        self.update_download_password = "1234"  # 下载密码

        # 首次运行标记
        self.first_run = True  # 默认设为true，load_config时会检查

        # 使用 QSettings 存储认证信息
        from PySide6.QtCore import QSettings
        self.settings = QSettings("WutheringWavesAchievement", "AuthData")

        self.load_config()
        self._load_auth_from_settings()

    def load_config(self):
        """加载配置"""
        try:
            if self.config_file.exists():
                with open(self.config_file, 'r', encoding='utf-8') as f:
                    loaded_data = json.load(f)
                    # 更新属性
                    for key, value in loaded_data.items():
                        if hasattr(self, key):
                            setattr(self, key, value)
                    # 如果配置文件存在，说明不是首次运行
                    self.first_run = False
            else:
                # 配置文件不存在，是首次运行
                self.first_run = True
        except Exception as e:
            logger.error("加载配置失败: %s", e)
            # 出错时也当作首次运行
            self.first_run = True

    def save_config(self):
        """保存配置"""
        try:
            # 确保目录存在
            self.config_file.parent.mkdir(parents=True, exist_ok=True)

            # 收集所有属性（不包括认证信息和内部配置）
            data = {
                "current_user": self.current_user,
                "users": self.users,
                "theme": self.theme,
                "auto_save": self.auto_save,
                "use_background": self.use_background,
                "custom_background_light": self.custom_background_light,
                "custom_background_dark": self.custom_background_dark,
                "current_profile": self.current_profile,
                "crawl_settings": self.crawl_settings,
                "user_avatars": self.user_avatars,
                "user_character_names": self.user_character_names,
                "first_run": False  # 保存后不再是首次运行
            }

            with open(self.config_file, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)

            # 保存认证信息到 QSettings
            self._save_auth_to_settings()
        except Exception as e:
            logger.error("保存配置失败: %s", e)

    def save_to_settings(self):
        """保存配置到设置文件（与模板保持一致的方法名）"""
        self.save_config()

    def add_user(self, username, user_data=None):
        """添加用户"""
        if user_data is None:
            user_data = {}
        self.users[username] = user_data
        self.current_user = username
        self.save_config()

    def switch_user(self, username):
        """切换用户"""
        if username in self.users:
            self.current_user = username
            self.save_config()
            # 发送用户切换信号
            from core.signal_bus import signal_bus
            signal_bus.user_switched.emit(username)
            return True
        return False

    def get_current_user(self):
        """获取当前用户"""
        return self.current_user

    def get_users(self):
        """获取所有用户"""
        return self.users

    def get_auth_data(self):
        """获取当前认证数据"""
        return self.devcode or "", self.token or ""

    def _load_auth_from_settings(self):
        """从 QSettings 加载认证信息"""
        self.devcode = self.settings.value("devcode", "", str)
        self.token = self.settings.value("token", "", str)

    def _save_auth_to_settings(self):
        """保存认证信息到 QSettings"""
        self.settings.setValue("devcode", self.devcode)
        self.settings.setValue("token", self.token)
        self.settings.sync()  # 立即保存

    def set_user_avatar(self, username, avatar_path):
        """设置用户头像"""
        self.user_avatars[username] = avatar_path
        self.save_config()

    def get_user_avatar(self, username):
        """获取用户头像"""
        return self.user_avatars.get(username, "")

    def get_current_user_avatar(self):
        """获取当前用户头像"""
        current_user = self.get_current_user()
        return self.get_user_avatar(current_user)

    def set_user_character_name(self, username, character_name):
        """设置用户角色名"""
        self.user_character_names[username] = character_name
        self.save_config()

    def get_user_character_name(self, username):
        """获取用户角色名"""
        character_name = self.user_character_names.get(username)
        if not character_name:
            avatar_path = self.get_user_avatar(username)
            if avatar_path:
                character_name = Path(avatar_path).stem
            else:
                character_name = "男漂泊者"
        return character_name

    def get_current_user_character_name(self):
        """获取当前用户角色名"""
        current_user = self.get_current_user()
        return self.get_user_character_name(current_user)

    def save_base_achievements(self, achievements):
        """保存基础成就数据"""
        base_file = get_resource_path("resources/base_achievements.json")
        try:
            # 只保存基础信息，排除用户相关的字段
            base_data = []
            for achievement in achievements:
                base_achievement = {
                    "绝对编号": achievement.get("绝对编号", ""),  # 仅用于排序
                    "版本": achievement.get("版本", ""),
                    "第一分类": achievement.get("第一分类", ""),
                    "第二分类": achievement.get("第二分类", ""),
                    "编号": achievement.get("编号", ""),  # 作为主键
                    "名称": achievement.get("名称", ""),
                    "描述": achievement.get("描述", ""),
                    "奖励": achievement.get("奖励", ""),
                    "是否隐藏": achievement.get("是否隐藏", "")
                }

                # 添加成就组相关字段（如果有）
                if achievement.get("成就组ID"):
                    base_achievement["成就组ID"] = achievement.get("成就组ID")
                if achievement.get("互斥成就"):
                    base_achievement["互斥成就"] = achievement.get("互斥成就")

                base_data.append(base_achievement)

            with open(base_file, 'w', encoding='utf-8') as f:
                json.dump(base_data, f, ensure_ascii=False, indent=2)
            logger.info("基础成就数据已保存到: %s", base_file)
            return True
        except Exception as e:
            logger.error("保存基础成就数据失败: %s", e)
            return False

    def load_base_achievements(self):
        """加载基础成就数据"""
        base_file = get_resource_path("resources/base_achievements.json")
        try:
            if base_file.exists():
                with open(base_file, 'r', encoding='utf-8') as f:
                    return json.load(f)
            else:
                logger.info("基础成就数据文件不存在，创建空文件")
                # 确保目录存在
                base_file.parent.mkdir(parents=True, exist_ok=True)
                # 创建空文件
                with open(base_file, 'w', encoding='utf-8') as f:
                    json.dump([], f, ensure_ascii=False, indent=2)
                return []
        except Exception as e:
            logger.error("加载基础成就数据失败: %s", e)
            return []

    def save_user_progress(self, username, progress_data):
        """保存用户进度数据"""
        # 获取用户的UID
        users = self.get_users()
        user_data = users.get(username, {})
        uid = user_data.get('uid', username) if isinstance(user_data, dict) else username

        progress_file = get_resource_path(f"resources/user_progress_{uid}.json")
        try:
            with open(progress_file, 'w', encoding='utf-8') as f:
                json.dump(progress_data, f, ensure_ascii=False, indent=2)
            return True
        except Exception as e:
            logger.error("保存用户进度数据失败: %s", e)
            return False

    def load_user_progress(self, username):
        """加载用户进度数据"""
        # 获取用户的UID
        users = self.get_users()
        user_data = users.get(username, {})
        uid = user_data.get('uid', username) if isinstance(user_data, dict) else username

        progress_file = get_resource_path(f"resources/user_progress_{uid}.json")
        try:
            if progress_file.exists():
                with open(progress_file, 'r', encoding='utf-8') as f:
                    return json.load(f)
            else:
                logger.info("用户 %s (UID: %s) 的进度数据文件不存在，创建空数据文件", username, uid)
                # 创建空的进度数据文件
                empty_progress = {}
                with open(progress_file, 'w', encoding='utf-8') as f:
                    json.dump(empty_progress, f, ensure_ascii=False, indent=2)
                return empty_progress
        except Exception as e:
            logger.error("加载用户进度数据失败: %s", e)
            return {}

    def save_category_config(self, category_config):
        """保存分类配置"""
        config_file = get_resource_path("resources/category_config.json")
        try:
            with open(config_file, 'w', encoding='utf-8') as f:
                json.dump(category_config, f, ensure_ascii=False, indent=2)
            logger.info("分类配置已保存到: %s", config_file)
            return True
        except Exception as e:
            logger.error("保存分类配置失败: %s", e)
            return False

    def load_category_config(self):
        """加载分类配置"""
        config_file = get_resource_path("resources/category_config.json")
        try:
            if config_file.exists():
                with open(config_file, 'r', encoding='utf-8') as f:
                    return json.load(f)
            else:
                logger.info("分类配置文件不存在，创建默认配置文件")
                default_config = self.get_default_category_config()
                # 创建默认配置文件
                self.save_category_config(default_config)
                return default_config
        except Exception as e:
            logger.error("加载分类配置失败: %s", e)
            return self.get_default_category_config()

    def get_default_category_config(self):
        """获取默认分类配置"""
        return {
            "first_categories": {
                "索拉漫行": 1,
                "长路留迹": 2,
                "铿锵刃鸣": 3,
                "诸音声轨": 4
            },
            "second_categories": {
                "索拉漫行": {
                    "索拉的大地·瑝珑": "10",
                    "瑝珑的足迹·一": "20",
                    "瑝珑的足迹·二": "30",
                    "黑海岸的足迹·一": "40",
                    "索拉的大地·黎那汐塔": "50",
                    "黎那汐塔的足迹·一": "60",
                    "黎那汐塔的足迹·二": "70",
                    "黎那汐塔的足迹·三": "80",
                    "荒野的呼唤": "90"
                },
                "长路留迹": {
                    "漂泊之旅": "10",
                    "漂泊之旅·一": "10",
                    "漂泊之旅·二": "20",
                    "与你的印迹": "30",
                    "世间百态": "40",
                    "世间百态·一": "40"
                },
                "铿锵刃鸣": {
                    "战斗的记忆": "10",
                    "战斗的技巧·一": "20",
                    "战斗的技巧·二": "30",
                    "战斗的技巧·三": "40",
                    "战斗的技巧·四": "50",
                    "战斗的回响": "60",
                    "意外体验": "70",
                    "来自深塔·一": "80",
                    "来自深塔·二": "90"
                },
                "诸音声轨": {
                    "成长之路": "10",
                    "别域的友谊": "20",
                    "声骸数据": "30"
                }
            }
        }

    def reencode_all_user_progress(self):
        """重新编码所有用户的存档数据和基础数据"""
        try:
            # 加载基础成就数据
            base_achievements = self.load_base_achievements()
            if not base_achievements:
                logger.error("基础成就数据为空，无法重新编码")
                return False

            # 导入成就管理器来重新编号
            from core.manage_tab import ManageTab
            manage_tab = ManageTab()

            # 先保存原始编号映射（使用成就名称+第一分类+第二分类作为唯一标识）
            original_mapping = {}
            for achievement in base_achievements:
                # 使用成就名称+第一分类+第二分类作为唯一标识
                name = achievement.get('名称', '')
                first_cat = achievement.get('第一分类', '')
                second_cat = achievement.get('第二分类', '')
                code = achievement.get('编号', '')
                abs_id = achievement.get('绝对编号', '')

                if name and first_cat and second_cat and code:
                    key = f"{name}|{first_cat}|{second_cat}"
                    original_mapping[key] = {
                        'code': code,
                        'abs_id': abs_id
                    }

            # 复制一份基础数据用于重新编号
            import copy
            achievements_for_reencode = copy.deepcopy(base_achievements)

            # 使用新的分类配置重新生成编号和绝对编号
            reencoded_achievements, old_to_new_id_map = manage_tab._smart_reencode_achievements(
                achievements_for_reencode)

            # 创建编号映射：旧编号 -> 新编号
            id_mapping = {}
            # 创建绝对编号映射：旧绝对编号 -> 新绝对编号
            abs_id_mapping = {}

            # 创建成就名称到新编号的映射
            name_to_new_id = {}
            for achievement in reencoded_achievements:
                name = achievement.get('名称', '')
                first_cat = achievement.get('第一分类', '')
                second_cat = achievement.get('第二分类', '')
                new_id = achievement.get('编号', '')
                new_abs_id = achievement.get('绝对编号', '')

                if name and first_cat and second_cat and new_id:
                    key = f"{name}|{first_cat}|{second_cat}"
                    name_to_new_id[key] = new_id

            # 为每个用户创建个性化的编号映射
            user_id_mappings = {}
            users = self.get_users()

            for username in users:
                user_progress = self.load_user_progress(username)
                if not user_progress:
                    continue

                user_id_mapping = {}
                logger.debug("为用户 %s 创建编号映射...", username)

                # 遍历用户进度中的每个成就
                for old_id in user_progress.keys():
                    # 在重新编码后的成就中查找匹配的成就
                    found = False
                    for achievement in reencoded_achievements:
                        if achievement.get('编号', '') == old_id:
                            # 如果编号没变，不需要映射
                            found = True
                            break

                    if not found:
                        # 编号变了，需要通过名称查找新编号
                        # 在原始成就中查找这个编号对应的成就
                        for achievement in base_achievements:
                            if achievement.get('编号', '') == old_id:
                                name = achievement.get('名称', '')
                                first_cat = achievement.get('第一分类', '')
                                second_cat = achievement.get('第二分类', '')
                                key = f"{name}|{first_cat}|{second_cat}"
                                new_id = name_to_new_id.get(key, '')

                                if new_id and new_id != old_id:
                                    user_id_mapping[old_id] = new_id
                                    logger.debug("用户 %s 编号映射: %s -> %s", username, old_id, new_id)
                                break

                user_id_mappings[username] = user_id_mapping

            # 使用第一个用户的映射作为全局映射（保持兼容性）
            if user_id_mappings:
                first_user = list(user_id_mappings.keys())[0]
                id_mapping = user_id_mappings[first_user]

            # 更新成就组的互斥成就列表
            logger.debug("开始更新成就组的互斥成就列表...")
            self._update_achievement_groups_mutex_relations(reencoded_achievements, id_mapping)
            logger.debug("成就组的互斥成就列表更新完成")

            # 检查基础成就数据是否被错误修改
            logger.debug("检查基础成就数据完整性...")
            for i, achievement in enumerate(reencoded_achievements):
                if i < 5:  # 只检查前5个
                    for key, value in achievement.items():
                        if isinstance(value, list) and key != '互斥成就':
                            logger.error("基础成就数据中发现非互斥成就的列表格式: %s -> %s, 值: %s", key, type(value), value)

            # 按绝对编号排序后再保存基础数据
            sorted_achievements = sorted(reencoded_achievements, key=lambda x: int(x.get('绝对编号', '0')))

            # 检查基础成就数据是否被错误修改
            logger.debug("检查基础成就数据完整性...")
            logger.debug("重新编码后的成就数量: %d", len(sorted_achievements))
            logger.debug("原始成就数量: %d", len(base_achievements))

            # 检查是否有成就丢失
            reencoded_codes = set(achievement.get('编号', '') for achievement in sorted_achievements)
            original_codes = set(achievement.get('编号', '') for achievement in base_achievements)

            missing_codes = original_codes - reencoded_codes
            if missing_codes:
                logger.error("重新编码后丢失的成就编号: %s", missing_codes)

            extra_codes = reencoded_codes - original_codes
            if extra_codes:
                logger.debug("重新编码后新增的成就编号: %s", extra_codes)

            for i, achievement in enumerate(reencoded_achievements):
                if i < 5:  # 只检查前5个
                    for key, value in achievement.items():
                        if isinstance(value, list) and key != '互斥成就':
                            logger.error("基础成就数据中发现列表格式: %s -> %s, 值: %s", key, type(value), value)
                        elif key == '互斥成就' and not isinstance(value, list):
                            logger.debug("互斥成就数据格式正常: %s -> %s, 值: %s", key, type(value), value)

            # 保存重新编码后的基础数据
            if not self.save_base_achievements(sorted_achievements):
                logger.error("保存重新编码后的基础成就数据失败")
                return False
            logger.info("基础成就数据已更新")

            # 获取所有用户
            users = self.get_users()
            updated_count = 0

            # 更新每个用户的进度数据
            for username in users:
                # 加载用户进度
                user_progress = self.load_user_progress(username)

                if not user_progress:
                    continue

                # 获取该用户的编号映射
                user_id_mapping = user_id_mappings.get(username, {})

                # 调试：检查用户进度数据
                logger.debug("处理用户: %s", username)
                logger.debug("用户进度数据类型: %s", type(user_progress))
                if isinstance(user_progress, dict):
                    logger.debug("用户进度键数量: %d", len(user_progress))
                    # 检查前几个键值对的格式
                    for i, (key, value) in enumerate(user_progress.items()):
                        if i < 3:  # 只检查前3个
                            logger.debug("键: %s, 值类型: %s, 值: %s", key, type(value), value)
                else:
                    logger.error("用户进度数据格式错误，期望dict，实际%s", type(user_progress))
                    continue

                # 创建新的进度数据
                new_progress = {}

                # 更新成就进度中的编号
                for old_id, progress_info in user_progress.items():
                    # 查找新编号（使用该用户的映射）
                    new_id = user_id_mapping.get(old_id, old_id)

                    # 检查progress_info的类型，确保是字典格式
                    if not isinstance(progress_info, dict):
                        logger.error("用户进度数据格式错误: %s -> %s, 期望dict", old_id, type(progress_info))
                        logger.error("进度信息内容: %s", progress_info)
                        logger.error("前一个键: %s", prev_key if 'prev_key' in locals() else 'None')
                        logger.error("前一个值类型: %s", type(prev_value) if 'prev_value' in locals() else 'None')
                        # 尝试修复格式
                        if isinstance(progress_info, list) and len(progress_info) > 0:
                            # 如果是列表，尝试使用第一个元素作为状态
                            logger.debug("修复列表格式: %s -> {'获取状态': %s}", progress_info, str(progress_info[0]))
                            progress_info = {'获取状态': str(progress_info[0])}
                        else:
                            # 其他情况，设置为未完成
                            logger.debug("修复其他格式: %s -> {'获取状态': '未完成'}", progress_info)
                            progress_info = {'获取状态': '未完成'}

                    # 使用新编号（如果有变化）或保持原编号
                    new_progress[new_id] = progress_info

                    # 如果编号有变化，增加计数
                    if new_id != old_id:
                        updated_count += 1

                    # 保存当前键值对，用于调试
                    prev_key = old_id
                    prev_value = progress_info

                # 检查是否有成就没有对应的进度
                if len(new_progress) < len(reencoded_achievements):
                    logger.warning("用户 %s 的进度数量(%d)少于成就数量(%d)",
                                   username, len(new_progress), len(reencoded_achievements))

                    # 找出缺失的成就
                    achievement_ids = set(achievement.get('编号', '') for achievement in reencoded_achievements)
                    progress_ids = set(new_progress.keys())
                    missing_ids = achievement_ids - progress_ids

                    if missing_ids:
                        logger.debug("缺失进度的成就编号: %s", sorted(list(missing_ids)))
                        # 为缺失的成就添加默认进度
                        for missing_id in missing_ids:
                            new_progress[missing_id] = {'获取状态': '未完成'}
                            logger.debug("为成就 %s 添加默认进度", missing_id)
                elif len(new_progress) > len(reencoded_achievements):
                    logger.warning("用户 %s 的进度数量(%d)多于成就数量(%d)",
                                   username, len(new_progress), len(reencoded_achievements))

                    # 找出多余的进度
                    achievement_ids = set(achievement.get('编号', '') for achievement in reencoded_achievements)
                    progress_ids = set(new_progress.keys())
                    extra_ids = progress_ids - achievement_ids

                    if extra_ids:
                        # 移除多余的进度
                        for extra_id in extra_ids:
                            del new_progress[extra_id]
                            logger.debug("移除多余的进度 %s", extra_id)

                # 按编号顺序排序后再保存用户进度
                sorted_progress = dict(sorted(new_progress.items(), key=lambda x: x[0]))

                # 保存更新后的进度
                if self.save_user_progress(username, sorted_progress):
                    logger.info("用户 %s 的进度数据已更新", username)
                else:
                    logger.error("保存用户 %s 的进度数据失败", username)

            logger.info("重新编码完成，共更新 %d 条记录", updated_count)
            return True

        except Exception as e:
            logger.error("重新编码用户进度数据失败: %s", e)
            return False

    def _update_achievement_groups_mutex_relations(self, achievements, id_mapping):
        """更新成就组的互斥成就列表"""
        try:
            # 收集所有成就组
            groups = {}
            for achievement in achievements:
                group_id = achievement.get('成就组ID')
                if group_id:
                    if group_id not in groups:
                        groups[group_id] = []
                    groups[group_id].append(achievement)

            # 为每个成就组更新互斥关系
            updated_groups = 0
            for group_id, members in groups.items():
                if len(members) < 2:
                    continue  # 至少需要2个成员才有互斥关系

                # 获取组内所有成员的新编号
                member_codes = [member.get('编号', '') for member in members]

                # 为每个成员更新互斥列表
                for member in members:
                    current_code = member.get('编号', '')
                    # 互斥列表 = 组内其他成员的新编号
                    mutex_list = [code for code in member_codes if code != current_code]
                    member['互斥成就'] = mutex_list

                updated_groups += 1



        except Exception as e:
            logger.error("更新成就组互斥关系失败: %s", e)


# 创建全局配置实例
config = Config()