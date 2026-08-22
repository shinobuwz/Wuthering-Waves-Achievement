# 数据来源与交换边界

## Purpose

导航项目的数据流和权威边界：随程序发布的成就元数据、旧版 Python 文件、原生版 generation、匿名 Kuro Wiki、JSON/TSV/XLSX 交换以及 OCR/运行时缓存如何进入工作区。

## Entry points

- 随程序发布的成就库：`resources/base_achievements.json`、`resources/category_config.json`；原生版适配器为 `native/src/Wuwa.Infrastructure/AchievementLibrarySources.cs`，旧版加载器为 `core/config.py`。
- 旧版档案：`resources/config.json`、`resources/user_progress_{uid}.json`；原生版只读适配器为 `native/src/Wuwa.Infrastructure/LegacyProfileSources.cs`。
- 原生版状态：`native/src/Wuwa.Infrastructure/Persistence.cs` 的 LocalAppData generation/manifest；工作区入口为 `native/src/Wuwa.Core/AchievementWorkspace.cs`。
- 远端来源：旧版 `core/crawl_tab.py` 和原生版 `native/src/Wuwa.Infrastructure/WikiSources.cs`；原生版对账位于 `native/src/Wuwa.Core/SyncWorkspace.cs`。
- 交换：`native/src/Wuwa.Infrastructure/AchievementExchangeFactory.cs`、`JsonAchievementExchange.cs`、`ExcelAchievementExchange.cs`，契约在 `native/src/Wuwa.Core/ExchangeContracts.cs`。
- Tests/runtime：`native/tests/Wuwa.Tests/WikiExchangeUpdateTests.cs`、`ShippedJsonAchievementLibrarySourceTests.cs`、`PersistenceAndMigrationTests.cs`；运行 `dotnet test`，真实 Wiki 仅用 `native/scripts/verify-wiki-live.ps1` 的临时 data root。

## Boundaries

- 随程序发布的元数据描述成就与分类；用户进度是另一类可变数据。缓存（如 `achievement_cache.json`、update cache）不是原生版权威状态。
- 旧版的 `编号` 是导入/导出兼容键；原生版读入后转换为 `AchievementId` 状态表，并记录来源元数据，不写回旧版文件。
- Wiki 数据行必须通过响应/结构/合理数量校验和保守的身份对账；远端移除先保留为 tombstone。
- 交换导入先解析为候选、校验字段/身份/状态/成就组引用，再按明确的 replace/merge 语义提交 generation；导出格式由扩展名显式选择。

## Read next

- 先读 `README.md` 和 `native/README.md`，确认双版本并行的数据说明和用户目录边界。
- 需要身份/同步时读 `Models.cs`、`AchievementIdentityTests.cs`、`WikiSources.cs`、`SyncWorkspace.cs`。
- 需要文件安全/恢复时读 `Persistence.cs` 与 `PersistenceAndMigrationTests.cs`。
- 需要交换兼容矩阵时读 `ExchangeContracts.cs`、两个交换适配器和 `WikiExchangeUpdateTests.cs`。
- `verified_against: commit:94aeb30`
