"""
鸣潮成就管理器 - 更新检查模块
检查GitHub仓库是否有新版本发布
"""

import logging
import requests
import json
from datetime import datetime, timedelta
from pathlib import Path
from packaging import version
import webbrowser
import os
import urllib3

logger = logging.getLogger(__name__)

# 禁用SSL警告
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

from core.config import config
from version import VERSION


class UpdateChecker:
    """
    鸣潮成就管理器更新检查器
    
    功能：
    1. 检查GitHub仓库是否有新版本
    2. 缓存检查结果，避免频繁请求
    3. 显示更新信息
    4. 提供下载链接
    """
    
    def __init__(self):
        """
        初始化更新检查器
        使用项目配置中的仓库信息和当前版本
        """
        # GitHub仓库信息（可在config.py中配置）
        self.repo_owner = getattr(config, 'github_owner', 'your-username')
        self.repo_name = getattr(config, 'github_repo', 'wuthering-waves-achievement')
        self.current_version = VERSION
        
        # GitHub API 基础URL
        self.api_base = "https://api.github.com"
        
        # 缓存文件路径（存储在项目resources目录）
        self.cache_dir = Path("resources")
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        self.cache_file = self.cache_dir / "update_cache.json"
        
        # 请求超时时间（秒）
        self.timeout = 10
        
        # 自定义User-Agent（GitHub要求）
        self.user_agent = f"WutheringWavesAchievement/{self.current_version}"
    
    def get_latest_release(self) -> dict:
        """获取最新的Release信息"""
        # 构建API URL
        api_url = f"{self.api_base}/repos/{self.repo_owner}/{self.repo_name}/releases/latest"
        
        # 设置请求头
        headers = {
            "Accept": "application/vnd.github.v3+json",  # 指定API版本
            "User-Agent": self.user_agent
        }
        
        try:
            logger.info("正在检查更新...")
            logger.info("API URL: %s", api_url)
            
            # 发送GET请求，禁用SSL验证（解决证书问题）
            response = requests.get(
                api_url, 
                headers=headers, 
                timeout=self.timeout,
                verify=False  # 禁用SSL证书验证
            )
            
            # 检查响应状态
            response.raise_for_status()  # 如果状态码不是200，抛出异常
            
            # 解析JSON响应
            release_data = response.json()
            
            logger.info("获取到Release: %s", release_data.get('tag_name'))
            return release_data

        except requests.exceptions.Timeout:
            logger.warning("请求超时，请检查网络连接")
            return None
        except requests.exceptions.HTTPError as e:
            if e.response.status_code == 404:
                logger.warning("未找到该仓库或没有Release")
            elif e.response.status_code == 403:
                # 可能是速率限制
                limit = e.response.headers.get('X-RateLimit-Limit', '?')
                remaining = e.response.headers.get('X-RateLimit-Remaining', '?')
                reset_time = e.response.headers.get('X-RateLimit-Reset', '?')
                logger.warning("API限制：%s/%s 次，重置时间：%s", remaining, limit, reset_time)
            else:
                logger.error("HTTP错误: %s", e)
            return None
        except requests.exceptions.RequestException as e:
            logger.error("网络请求失败: %s", e)
            return None
        except json.JSONDecodeError as e:
            logger.error("JSON解析失败: %s", e)
            return None
    
    def parse_version(self, version_str: str) -> version.Version:
        """
        解析版本字符串
        
        GitHub的tag_name可能有多种格式：
        - v1.0.0
        - version-1.0.0
        - 1.0.0
        - release-v1.0.0
        
        这个方法会清理前缀，提取纯版本号
        """
        # 移除常见的前缀
        prefixes = ['v', 'V', 'version', 'release-', 'ver.']
        clean_version = version_str
        
        for prefix in prefixes:
            if clean_version.lower().startswith(prefix.lower()):
                clean_version = clean_version[len(prefix):]
                # 如果移除前缀后以-或_开头，继续移除
                if clean_version.startswith(('-', '_')):
                    clean_version = clean_version[1:]
        
        # 使用packaging.version解析
        try:
            return version.parse(clean_version)
        except version.InvalidVersion:
            logger.warning("无法解析版本号: %s", version_str)
            return version.parse("0.0.0")
    
    def compare_versions(self, latest_version_str: str) -> dict:
        """比较版本号"""
        # 解析版本
        current_ver = self.parse_version(self.current_version)
        latest_ver = self.parse_version(latest_version_str)
        
        logger.info("当前版本: %s", current_ver)
        logger.info("最新版本: %s", latest_ver)
        
        # 比较版本
        result = {
            "current_version": str(current_ver),
            "latest_version": str(latest_ver)
        }
        
        if latest_ver > current_ver:
            result.update({
                "has_update": True,
                "is_major": latest_ver.major > current_ver.major,
                "is_minor": latest_ver.minor > current_ver.minor,
                "is_patch": latest_ver.micro > current_ver.micro,
                "update_type": self._get_update_type(current_ver, latest_ver)
            })
        elif latest_ver < current_ver:
            # 本地版本比最新版本还新（可能是开发版）
            result.update({"has_update": False, "is_dev": True})
        else:
            result.update({"has_update": False, "is_latest": True})
        
        return result
    
    def _get_update_type(self, current: version.Version, latest: version.Version) -> str:
        """获取更新类型"""
        if latest.major > current.major:
            return "major"  # 主要版本更新（可能不兼容）
        elif latest.minor > current.minor:
            return "minor"  # 次要版本更新（新增功能）
        else:
            return "patch"  # 补丁更新（修复bug）
    
    def check_with_cache(self, force_check: bool = False) -> dict:
        """
        带缓存的更新检查
        
        Args:
            force_check: 是否强制检查（忽略缓存）
        
        缓存策略：
        1. 如果force_check为True，强制检查
        2. 读取缓存文件中的timestamp
        3. 如果读取不到timestamp或超过24小时，检查github
        4. 检查github后保存timestamp
        5. 新增：检查当前版本与缓存中的版本是否一致，如果不一致说明已更新，重新检查
        """
        # 1. 检查是否需要跳过缓存
        if force_check:
            logger.info("强制检查更新...")
            return self._check_and_cache()

        # 2. 检查缓存文件是否存在
        if not self.cache_file.exists():
            logger.info("无缓存，开始检查更新...")
            return self._check_and_cache()
        
        # 3. 读取缓存
        try:
            with open(self.cache_file, 'r', encoding='utf-8') as f:
                cache_data = json.load(f)
            
            update_info = cache_data.get('update_info', {})
            cached_current_version = update_info.get('current_version')
            cached_latest_version = update_info.get('latest_version')
            
            # 检查缓存中的当前版本是否与实际版本一致
            if cached_current_version and cached_current_version != self.current_version:
                logger.info("检测到版本变化：%s -> %s，重新检查更新", cached_current_version, self.current_version)
                # 版本发生变化，需要重新检查更新
                return self._check_and_cache()
            
            # 首先检查缓存时间
            cache_time = datetime.fromisoformat(cache_data.get('timestamp', '2000-01-01'))
            now = datetime.now()
            
            # 如果缓存超过24小时，重新检查
            if now - cache_time > timedelta(hours=24):
                logger.info("缓存过期，重新检查...")
                return self._check_and_cache()
            
            # 检查版本一致性
            if cached_latest_version:
                # 如果当前版本等于缓存中的最新版本，说明已经是最新版本
                if self.parse_version(self.current_version) >= self.parse_version(cached_latest_version):
                    logger.info("当前版本%s已是最新，使用缓存", self.current_version)
                    # 直接返回缓存结果，不更新时间戳
                    update_info['current_version'] = self.current_version  # 确保当前版本正确
                    update_info['has_update'] = False
                    update_info['is_latest'] = True
                    return update_info
            
            # 使用缓存，但确保current_version是最新的
            update_info['current_version'] = self.current_version
            logger.info("使用缓存数据...")
            return update_info

        except (json.JSONDecodeError, KeyError, ValueError) as e:
            logger.warning("缓存读取失败: %s，重新检查...", e)
            return self._check_and_cache()
    
    def _check_and_cache(self) -> dict:
        """检查更新并缓存结果"""
        # 获取最新Release
        release_data = self.get_latest_release()
        
        if not release_data:
            # 如果获取失败，返回空结果，但仍包含当前版本信息
            result = {
                "has_update": False, 
                "error": "无法获取更新信息",
                "current_version": self.current_version,
                "latest_version": self.current_version  # 无法获取最新版本时使用当前版本
            }
            self._save_cache(result)
            return result
        
        # 比较版本
        version_comparison = self.compare_versions(release_data.get('tag_name', '0.0.0'))
        
        # 构建完整结果，确保始终包含版本信息
        result = {
            **version_comparison,
            "current_version": self.current_version,
            "latest_version": release_data.get('tag_name', '0.0.0'),
            "release_info": {
                "tag_name": release_data.get('tag_name'),
                "name": release_data.get('name', ''),
                "body": release_data.get('body', ''),
                "html_url": release_data.get('html_url', ''),
                "published_at": release_data.get('published_at', ''),
                "prerelease": release_data.get('prerelease', False),
                "assets_count": len(release_data.get('assets', []))
            },
            "checked_at": datetime.now().isoformat(),
            "repository": f"{self.repo_owner}/{self.repo_name}"
        }
        
        # 保存到缓存
        self._save_cache(result)
        
        return result
    
    def _save_cache(self, update_info: dict):
        """保存检查结果到缓存"""
        # 确保update_info包含正确的current_version
        if 'current_version' not in update_info:
            update_info['current_version'] = self.current_version
        
        cache_data = {
            "timestamp": datetime.now().isoformat(),
            "update_info": update_info,
            "repository": f"{self.repo_owner}/{self.repo_name}"
        }
        
        try:
            with open(self.cache_file, 'w', encoding='utf-8') as f:
                json.dump(cache_data, f, indent=2, ensure_ascii=False)
            logger.info("缓存已保存: %s", self.cache_file)
        except Exception as e:
            logger.error("缓存保存失败: %s", e)
    
    def display_update_info(self, update_info: dict):
        """显示更新信息"""
        if not update_info.get('has_update'):
            if update_info.get('is_latest'):
                logger.info("已经是最新版本！")
            elif update_info.get('is_dev'):
                logger.info("当前是开发版本")
            else:
                logger.info("无需更新")
            return

        # 有更新时的显示
        logger.info("=" * 60)
        logger.info("发现新版本！")
        logger.info("=" * 60)

        # 版本信息
        logger.info("当前版本: %s", update_info['current_version'])
        logger.info("最新版本: %s", update_info['latest_version'])

        # 更新类型
        update_type = update_info.get('update_type', 'patch')
        type_emojis = {
            "major": "主要更新（可能包含不兼容变更）",
            "minor": "功能更新",
            "patch": "修复更新"
        }
        logger.info("更新类型: %s", type_emojis.get(update_type, '更新'))

        # Release信息
        release = update_info.get('release_info', {})
        if release.get('name'):
            logger.info("发布名称: %s", release['name'])

        if release.get('published_at'):
            pub_date = release['published_at'][:10]  # 只取日期部分
            logger.info("发布时间: %s", pub_date)

        # 更新内容
        if release.get('body'):
            body = release['body'].strip()
            # 限制显示长度
            if len(body) > 500:
                body = body[:500] + "..."
            logger.info("更新内容:")
            logger.info("-" * 40)
            logger.info("%s", body)
            logger.info("-" * 40)

        # 下载信息 - GitHub Release
        release = update_info.get('release_info', {})
        download_url = release.get('html_url') or f"https://github.com/{self.repo_owner}/{self.repo_name}/releases/latest"
        logger.info("下载链接: %s", download_url)

        logger.info("=" * 60)

        # 询问用户是否打开下载页面
        try:
            response = input("\n是否打开下载页面？(y/N): ").strip().lower()
            if response == 'y':
                webbrowser.open(download_url)
                logger.info("已打开浏览器...")
        except EOFError:
            logger.info("下载链接: %s", download_url)
    
    def check_and_notify(self, force_check: bool = False):
        """检查并通知的主方法"""
        logger.info("检查 %s/%s 的更新...", self.repo_owner, self.repo_name)
        
        # 检查更新
        update_info = self.check_with_cache(force_check)
        
        # 显示结果
        self.display_update_info(update_info)
        
        return update_info


# ============================================================================
# 项目集成函数
# ============================================================================

def check_for_updates():
    """检查更新的便捷函数，可在应用启动时调用"""
    checker = UpdateChecker()
    return checker.check_and_notify()

def check_for_updates_background():
    """后台检查更新，不阻塞主线程"""
    import threading
    
    def check():
        try:
            checker = UpdateChecker()
            update_info = checker.check_with_cache()
            if update_info.get('has_update'):
                # 可以通过信号总线发送更新通知
                from core.signal_bus import signal_bus
                signal_bus.update_available.emit(update_info)
        except Exception as e:
            logger.error("后台更新检查失败: %s", e)
    
    # 在新线程中检查，避免阻塞启动
    thread = threading.Thread(target=check, daemon=True)
    thread.start()

def get_update_info():
    """获取更新信息，不显示通知"""
    checker = UpdateChecker()
    return checker.check_with_cache()

