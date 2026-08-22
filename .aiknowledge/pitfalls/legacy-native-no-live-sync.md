# 并行版本不能做隐藏双向同步

## 不要这样做

不要让 Native watcher、后台任务或“智能合并”持续改写 Legacy 配置/进度，也不要把两个实现的用户进度目录当成同一个实时数据库。

## 反例

Native 启动后监视 `resources/user_progress_{uid}.json`，发现变化就自动覆盖 Native 当前 generation；或者 Native 导入时直接编辑 legacy JSON 以“补齐”缺失字段。

## 正例

把 legacy `resources/config.json` 和选中的 `resources/user_progress_{uid}.json` 当作只读输入。先发现候选、展示昵称/UID/来源和数量，再由用户明确确认一次性导入；后续 Native 重新导入仍是显式 replace，并保留原 Native generation。

## 为什么不行

两套实现有不同的身份、状态和持久化边界。隐藏同步会制造双向写入竞态、无法判断覆盖方向的状态分叉，并让升级或删除便携程序目录产生不可预测的数据损失。Native 的 migration contract 也要求未知 legacy code 在激活前失败，而不是半写入修复源文件。

## 适用前提

当任务涉及 Native migration、portable lifecycle、legacy profile、用户进度或两个应用并行发布时适用。不适用于只读 fixture 解析、展示 legacy 文件内容或明确的最终 cutover 设计；即使未来 cutover，也必须由独立变更定义新的权威数据边界。

## 验证

回读 `src/Wuwa.Infrastructure/LegacyProfileSources.cs`、`src/Wuwa.Core/Contracts.cs` 中的只读接口和 `AchievementWorkspace.ImportLegacyProfileAsync`。运行 `tests/Wuwa.Tests/AchievementWorkspaceTests.cs` 与 `PersistenceAndMigrationTests.cs` 中的 `LegacySource_ReadsProfilesWithoutChangingLegacyFiles`、legacy discovery/unknown-code tests，并用复制出的 fixture 运行 `scripts/verify-portable-lifecycle.ps1`；不要把脚本指向正式用户文件。

## 重审条件

当 Native cutover 明确完成、legacy 文件不再作为可运行应用的状态源，或 migration contract 改为新的跨版本同步协议时，重新审查本条。
