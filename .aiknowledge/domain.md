# Domain Language

## Native 应用实现

**Meaning**：源码位于仓库根目录、由 WPF/.NET 8 构成的 Windows 原生应用，由 WPF 界面、`Wuwa.Core` 工作区领域层、`Wuwa.Infrastructure` 适配器和 C++ OCR 动态库组成。它是仓库唯一维护的应用运行时。

**Avoid**：不要把 Native 简化为 WPF 界面；`AchievementWorkspace` 才是管理、迁移、同步、交换、统计、状态变更和 OCR 结果合并的公共行为入口。

**Relations**：界面通过 Core 工作区入口调用命令和查询，文件、HTTP、旧数据导入、Win32 捕获和 C ABI 由 Infrastructure 适配；可变状态存放在 `%LocalAppData%\\WutheringWavesAchievement`。

## Legacy 导入数据

**Meaning**：仓库中保留的 `resources/config.json` 与 `resources/user_progress_{uid}.json` 是旧数据格式，仅作为 Native 的显式、只读、一次性迁移输入，不是另一套应用运行时。

**Avoid**：不要修改 legacy 文件，不要把 legacy 文件当作 Native 当前状态，也不要建立后台监视或双向同步。

**Relations**：Native 导入后将兼容编号映射为 `AchievementId`，可变状态存放在 `%LocalAppData%\\WutheringWavesAchievement` 的 generation 集合中。

## 原生成就身份（`AchievementId`）

**Meaning**：原生工作区使用的不可变成就身份。旧版导入记录由旧版 `编号` 确定性派生；接受新的 Wiki 数据行时由稳定的 `WikiSourceRef` 确定性派生。显示名称、分类、描述、Wiki 重新排列或原生版兼容编号变化，不应随意改变它。

**Avoid**：不要用名称、当前排序号或可重新编码的 `编号` 代替原生成就身份，也不要把 `AchievementId` 当成用户可见的导出编号。

**Relations**：`AchievementId` 是原生版状态表、OCR 候选、Wiki 对账和 generation 持久化的连接键；兼容编号仍用于旧版兼容和交换。

## 兼容编号（`LegacyCode`）

**Meaning**：现有数据模型中的 `编号` 兼容字段。legacy 导入数据使用它关联用户进度；原生版保留它以支持兼容导入导出，但它不是原生版的不可变身份。

**Avoid**：不要假设分类重排、旧版重新编码或 Wiki 新增记录后兼容编号永远稳定。

**Relations**：旧版进度先通过兼容编号导入，再映射到 `AchievementId`；原生版与远端同步时优先保留已匹配记录的原生成就身份和兼容编号。

## 成就获取状态

**Meaning**：项目认可的状态集合：`未完成`、`已完成`、`暂不可获取`、`已占用`。其中 `已占用` 表示互斥成就组中未被选择的成员，不是普通的“未完成”别名。

**Avoid**：不要静默把未知状态转换成默认值，也不要在成就组内把每一行当作完全独立的状态。

**Relations**：`AchievementWorkspace` 负责状态转换和统计；OCR、旧版导入、JSON/Excel 交换都必须先进入这套状态语义，再通过同一工作区规则落盘。

## 成就组

**Meaning**：由 `GroupId` 表示的关联成就集合。互斥选择组在一个成员完成时把其它成员置为 `已占用`；累计进度组表示逐级阈值，完成较高等级意味着较低等级也已跨过阈值。成就组只约束状态转换，统计和总数按每条原始成就行计数，不把组折算成一条。

**Avoid**：不要只更新选中的单行、把 `已占用` 当作完成，或让导入/OCR 绕过工作区的成就组状态转换。

**Relations**：成就组规则集中在 `AchievementWorkspace`；Wiki 解析、标注脚本和独立测试数据提供组元数据，界面与 OCR 只提交状态请求。

## Wiki 来源引用（`WikiSourceRef`）

**Meaning**：远端 Wiki 表格行的来源引用，当前形态为 `<entry-id>/<table-data-uid>/<row-data-index>`。它是 Wiki 对账的首选匹配依据；Wiki 重建表格时它可能变化，因此不是无条件永久身份。

**Avoid**：不要仅凭名称或任意模糊候选覆盖现有进度，也不要把 Wiki 来源引用变化自动解释成删除并丢弃进度。

**Relations**：匹配优先级是精确来源引用、唯一归一化完整签名、仅在旧版首次导入时使用的唯一名称+描述回退；歧义记录被隔离，远端移除先保留为 tombstone。

## 原生工作区快照（generation）

**Meaning**：原生版可变工作区的一次完整、版本化快照，包含成就库、分类、状态、设置、身份映射、tombstone 和 metadata；快照完整写入并验证后，原子替换 `current.json` 指针才成为当前状态。

**Avoid**：不要把单个缓存文件、半写入目录或尚未提交的 generation 当成权威状态。

**Relations**：generation 是原生版事务、故障恢复和显式替换的边界；至少保留多个有效快照供恢复，发布目录中的只读资源不属于用户可变快照。

## OCR 预览

**Meaning**：OCR 扫描产生的、仍待人工确认的候选结果集合，包含匹配名称、OCR 原文、置信度、推断状态和未匹配/歧义信息。它不是已经写入的进度。

**Avoid**：不要在扫描每一页时直接改写用户进度，不要把未知状态、未访问分类或取消产生的部分结果自动当成未完成。

**Relations**：原生版 OCR 预览通过 `AchievementWorkspace.ApplyOcrPreviewAsync` 在显式确认后合并为一个新 revision，并默认阻止已完成状态被低置信度结果降级。
