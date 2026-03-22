## 为什么

当前爬取流程中，API 响应被缓存到 `achievement_cache.json` 后永不过期，用户必须手动点击"清除缓存"再重新爬取才能获取最新数据。而 API 返回的数据中已包含 `lastUpdateTime` 字段（如 `2026-03-19`），完全可以利用该字段自动判断远端数据是否有更新，省去手动操作成本。

## 变更内容

- **新增**：启动爬取时，自动比对远端 `lastUpdateTime` 与本地缓存时间，若远端更新则自动刷新缓存
- **新增**：缓存文件中记录 `lastUpdateTime` 元数据，作为后续比对依据
- **新增**：提供轻量级的"仅检查更新"请求，避免每次都拉取完整数据
- **修改**：调整 `get_achievement_data()` 的缓存命中逻辑，从"缓存存在即使用"改为"缓存存在且未过期才使用"

## 功能 (Capabilities)

### 新增功能

- `cache-freshness-check`: 缓存新鲜度检测机制——在爬取前通过轻量请求获取远端 `lastUpdateTime`，与本地缓存记录的时间比对，决定是否需要重新拉取完整数据

### 修改功能

（无现有规范需要修改）

## 影响

- **代码**：`core/crawl_tab.py` 中的 `AchievementCrawler.get_achievement_data()` 方法需要重构缓存判断逻辑
- **数据**：`resources/achievement_cache.json` 结构需扩展，增加 `_cache_meta` 元数据字段记录缓存时间
- **网络**：新增一次轻量 API 请求用于获取更新时间（与现有请求相同 endpoint，但仅解析时间字段）
- **用户体验**：爬取操作变为自动感知更新，减少手动"清除缓存"的操作频率
