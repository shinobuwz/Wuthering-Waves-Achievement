# Domain Language

## Native 应用实现

**Meaning**：源码位于仓库根目录、由 WPF/.NET 8 构成的 Windows 原生应用，由模块化 WPF 应用壳、`Wuwa.Core` 领域层、`Wuwa.Infrastructure` 适配器和 C++ OCR 动态库组成。它是仓库唯一维护的应用运行时。

**Avoid**：不要把 Native 简化为 WPF 界面；成就行为仍以 `AchievementWorkspace` 为公共入口，连招行为以独立的 `RotationRunSession` 和只读运行时契约为边界。

**Relations**：界面通过 Core 入口调用命令和查询，文件、HTTP、旧数据导入、Win32 捕获/观察和 C ABI 由 Infrastructure 适配。可变状态默认存放在 `<程序目录>\\data`，测试或显式运行可用 `WUWA_NATIVE_DATA_ROOT` 覆盖；成就 generation 与 `rotations/` 连招状态彼此独立。

## Legacy 导入数据

**Meaning**：仓库中保留的 `resources/config.json` 与 `resources/user_progress_{uid}.json` 是旧数据格式，仅作为 Native 的显式、只读、一次性迁移输入，不是另一套应用运行时。

**Avoid**：不要修改 legacy 文件，不要把 legacy 文件当作 Native 当前状态，也不要建立后台监视或双向同步。

**Relations**：Native 导入后将兼容编号映射为 `AchievementId`，可变状态存放在当前 Native data root 的 generation 集合中；默认 data root 是 `<程序目录>\\data`，可由 `WUWA_NATIVE_DATA_ROOT` 覆盖。

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

## 模块化应用壳

**Meaning**：Native 主窗口的左侧导航与页面生命周期边界。启动进入总览，并路由到成就管理、连招助手、游戏工具、设置和帮助；`MainWindow` 负责导航、主题、全局快捷键和跨页面协调，不重新实现页面领域规则。

**Avoid**：不要在总览或壳层复制成就统计、状态转换、连招状态机、地图或唤取链接领域逻辑；行为等价控件应保留稳定的 UI Automation 入口。

**Relations**：成就视觉面由 `AchievementWorkspaceView` 承载但仍调用 `AchievementWorkspace`；连招页面调用独立 Rotation Core/Infrastructure；地图和唤取链接属于游戏工具。

## 连招助手

**Meaning**：Native 中“只提示、不代替操作”的前台辅助模块。它只观察用户真实键盘/鼠标输入，在验证过的《鸣潮》窗口位于前台时推进三步提示浮窗。

**Avoid**：不得发送或模拟游戏输入，不得读取/写入游戏进程内存，不得吞掉玩家输入；低级 Hook 必须继续调用 `CallNextHookEx`，并忽略注入标记事件。

**Relations**：`RotationRunSession` 是公共行为 seam；`WindowsRotationInputSource`、游戏窗口 monitor、`RotationRuntimeCoordinator` 和 NoActivate/点击穿透浮窗共同实现运行生命周期。Alt-Tab 暂停隐藏，`Ctrl+Shift+F11` 固定停止并恢复连招页。

## 连招流程

**Meaning**：带 `schemaVersion` 的 Native JSON 配置，由队伍槽位、角色别名、初始槽位、一次性 Opener 和可选 Loop 构成，并可包含安全的相对图标引用。

**Avoid**：不要把流程或绑定写进成就 generation，不要保留旧 Hekili 绝对图标路径，也不要监视或双向同步旧文件。

**Relations**：流程和绑定位于当前 Native data root 的 `rotations/profiles/` 与 `rotations/settings.json`；wuwa-Hekili JSON 仅通过用户显式选择执行一次性、只读、完整验证后原子导入。

## 游戏工具

**Meaning**：与成就状态和连招状态均无直接领域关系的独立工具页面。本项目当前只承载已有的“获取唤取链接”和“覆盖地图”能力。

**Avoid**：不要把游戏工具当作新增抽卡历史解析/导出入口，也不要让地图或唤取链接修改成就或连招状态。

**Relations**：工具页面只路由现有 Core/Infrastructure 行为；成就管理和连招助手保持各自的数据与运行生命周期。
