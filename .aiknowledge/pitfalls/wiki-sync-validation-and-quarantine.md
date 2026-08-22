# Wiki 同步必须验证并隔离歧义

## 不要这样做

不要因为 HTTP 返回 2xx 或解析出了少量行，就直接替换当前成就库；也不要在多个候选同分、source reference 缺失或字段不完整时凭名称猜测并迁移已有进度。

## 反例

把 malformed/partial Wiki response 当作成功，或使用“第一个名称相同的记录”匹配远端 row，随后立即删除未匹配旧记录并激活新状态。

## 正例

先验证 HTTP、business success、HTML/table schema、必需字段和相对当前库的合理行数；匹配按 exact `WikiSourceRef`、唯一归一化完整签名、仅用于 legacy bootstrap 的唯一名称+描述回退排序。重复或歧义 row quarantine，当前 active state 保持不变；远端移除先转 tombstone，不立刻丢弃进度。

## 为什么不行

Wiki 页面可能返回认证错误、截断内容、空模块、表格重建或重复候选。错误匹配会把旧的 `AchievementId`、`LegacyCode` 和 progress 绑定到错误成就；立即删除还会摧毁可恢复的用户状态。同步必须把“不确定”与“确认删除”区分开。

## 适用前提

当任务涉及 Kuro Wiki、匿名拉取、缓存刷新、source identity、分类变化、远端删除或成就库更新时适用。不适用于已经经过固定 fixture 校验的本地导入；本地交换仍必须执行自己的 schema、identity 和 status validation。

## 验证

回读 `native/src/Wuwa.Infrastructure/WikiSources.cs` 的 response/schema parsing 和 `native/src/Wuwa.Core/SyncWorkspace.cs` 的 validation、match precedence、quarantine 与 tombstone 分支。运行 `native/tests/Wuwa.Tests/WikiExchangeUpdateTests.cs` 中的 malformed choice、business failure、ambiguous signature、bootstrap identity retention tests；真实探针只能用临时 `WUWA_NATIVE_DATA_ROOT` 运行 `native/scripts/verify-wiki-live.ps1`。

## 重审条件

当 Wiki API 的稳定身份协议、响应 schema 或同步产品语义改为服务器提供不可变 ID/正式删除确认时，重新审查匹配优先级和 tombstone 策略。
