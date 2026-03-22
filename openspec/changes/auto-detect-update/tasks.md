## 1. 缓存元数据支持

- [x] 1.1 在 `AchievementCrawler.get_achievement_data()` 的 API 响应保存逻辑中，写入缓存时从响应 `data` 层提取 `lastUpdateTime`，连同当前时间戳组成 `_cache_meta` 对象，添加到缓存 JSON 的顶层
- [x] 1.2 新增 `_read_cache_meta(cache_file)` 辅助方法，从缓存文件读取 `_cache_meta` 字段，缺失时返回 `None`
- [x] 1.3 新增 `_extract_remote_update_time(response_data)` 辅助方法，从 API 响应中提取 `lastUpdateTime` 字段，缺失时返回 `None`

## 2. 缓存新鲜度检查流程

- [x] 2.1 重构 `get_achievement_data()` 方法：缓存文件不存在或无 `_cache_meta` 时，直接请求 API 并保存缓存（含元数据）
- [x] 2.2 重构 `get_achievement_data()` 方法：缓存存在且有 `_cache_meta` 时，先请求 API 获取最新响应，提取远端 `lastUpdateTime` 与本地比对
- [x] 2.3 比对结果为相同时，丢弃新响应，返回本地缓存数据
- [x] 2.4 比对结果为远端更新时，用新响应覆盖缓存文件（含更新的 `_cache_meta`），返回新数据

## 3. 降级与容错

- [x] 3.1 API 请求失败（网络异常）时，若本地有缓存则降级使用缓存数据，通过 `progress` 信号通知用户
- [x] 3.2 认证信息缺失时，若本地有缓存则直接使用缓存，无缓存则提示用户配置认证信息
- [x] 3.3 远端响应中 `lastUpdateTime` 字段缺失时，视为无法判断，重新拉取并覆盖缓存

## 4. 用户反馈

- [x] 4.1 在开始检查远端更新时发出进度信号"正在检查数据更新..."
- [x] 4.2 缓存命中（数据未更新）时发出进度信号"数据已是最新（远端更新时间: {lastUpdateTime}），使用本地缓存"
- [x] 4.3 缓存失效（数据已更新）时发出进度信号"检测到数据更新（{oldTime} → {newTime}），正在重新获取..."
- [x] 4.4 降级使用缓存时发出进度信号"网络请求失败，使用本地缓存"

## 5. 启动自动同步

- [x] 5.1 在 `MainWindow` 中新增 `setup_data_freshness_check()` 方法，启动 5 秒后通过 `QTimer.singleShot` 触发数据检查
- [x] 5.2 新增 `_check_data_freshness()` 方法，创建 `AchievementCrawler` 和 `CrawlerThread`，在后台线程执行同步
- [x] 5.3 新增 `_sync_crawl()` 方法，解析远端全量成就数据（不过滤版本），按 `(名称, clean_desc(描述))` 键与本地对比，识别新增和已删除成就
- [x] 5.4 新增 `_on_sync_finished()` 方法，在主线程中执行成就合并：检测新分类、合并新增、移除已删除、重新编号

## 6. Bug 修复

- [x] 6.1 修复持久化顺序 bug：将 `save_to_json()` 调用移到 `reencode_all_user_progress()` 之前，确保用户存档重编码时读取的是最新数据
- [x] 6.2 修复缓存更新未同步到成就数据库：`get_achievement_data()` 仅更新缓存文件，启动同步需额外解析并合并到 `base_achievements.json`
- [x] 6.3 修复 `_sync_remote_keys` 误触发合并：无变化时将 `_sync_remote_keys` 设为 `None` 而非保留非空 set，避免空列表但 `remote_keys` 为真值时触发不必要的移除逻辑
- [x] 6.4 新增 `_compute_data_hash()` 和内容级 hash 比对，解决远端时间戳未变但内容已更新的检测盲区

## 7. 验证

- [x] 7.1 手动测试：删除缓存文件后爬取，验证缓存文件中包含 `_cache_meta` 字段
- [x] 7.2 手动测试：不修改远端数据再次爬取，验证使用本地缓存且显示"数据已是最新"
- [x] 7.3 手动测试：断网后爬取，验证降级使用本地缓存且显示降级提示
- [x] 7.4 手动测试：使用旧版本无 `_cache_meta` 的缓存文件爬取，验证自动重新请求
- [x] 7.5 手动测试：启动应用后观察日志，验证 5 秒后自动同步且合并结果正确
- [x] 7.6 手动测试：验证持久化顺序正确——同步后 `base_achievements.json` 和用户存档数据一致
