# Native 状态必须以完整 generation 原子激活

## 不要这样做

不要分别原地写入状态、成就库、设置和进度，也不要在 generation 尚未完整写入和验证前先更新 `current.json`。

## 反例

保存状态时直接覆盖一个共享 `state.json`，或先把 manifest 指向新目录，再异步写入该目录的其余字段；启动恢复时又从任意最新目录或非权威 cache 猜测当前状态。

## 正例

在临时目录写入完整 versioned state，flush 后重新读取并验证 candidate，再提升为 final generation；最后以原子 manifest replacement 激活 `current.json`。启动时只选择合法、已提交且能通过校验的 generation，保留有效旧 generation 作为恢复路径。

## 为什么不行

崩溃、断电、杀进程或文件系统错误可能发生在任意写入边界。分散文件更新会让成就元数据、status map 和 metadata 互相错 revision；提前指针替换会把半成品暴露给下一次启动。缓存和 orphan 目录不能证明 activation 已提交。

## 适用前提

当修改 Native persistence、status/import/sync/OCR merge、manifest recovery、generation retention 或 portable data root 时适用。不适用于随程序发布的 immutable resources，也不要求把非权威的网络缓存纳入同一事务。

## 验证

回读 `src/Wuwa.Infrastructure/Persistence.cs` 的 temporary directory、candidate validation、manifest replacement、commit marker 和 recovery 分支。运行 `tests/Wuwa.Tests/PersistenceAndMigrationTests.cs` 中的 pre-commit/post-commit fault injection、malformed generation recovery、uncommitted orphan 和 retention tests，以及 `AchievementWorkspaceTests.JsonStore_RoundTripsStatusAndRetainsGenerations`。

## 重审条件

如果持久化格式改为数据库或另一个具有等价原子 snapshot/commit 语义的存储，必须重新证明“完整快照先验证、权威指针后激活、旧状态可恢复”仍成立。
