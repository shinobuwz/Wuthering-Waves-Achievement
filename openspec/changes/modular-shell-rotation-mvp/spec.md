# Modular Shell and Rotation MVP

## Intent

将现有 Native WPF 应用从单一成就工作区窗口重组为模块化应用壳，并交付一个可实际运行的“连招助手”MVP。连招助手只观察用户真实键鼠输入并推进提示，不发送游戏输入、不读取游戏内存、不代替玩家操作。

Change ID：`modular-shell-rotation-mvp`
Branch：`opsx/modular-shell-rotation-mvp`
Base：`master@1240c7384c3b842b5827c144cca814f320eb8245`

## Domain Language

### 成就管理

由 `AchievementWorkspace` 继续拥有成就库、状态、统计、追踪、OCR 结果合并、Legacy 导入、Wiki 同步和交换行为。页面重组不得绕过现有工作区公共入口。

### 连招助手

Native 应用中的独立功能模块。它加载显式连招流程，以只读方式观察用户物理输入，并在连招提示浮窗中显示当前动作和后续动作。它不调用输入注入接口，不读写游戏进程内存。

### 连招流程

由队伍配置、初始角色槽位、一次性启动轴（Opener）和可选循环轴（Loop）组成的版本化配置。流程在 Native data root 中拥有独立存储，不属于成就 generation。

### 游戏工具

与成就状态和连招状态均无直接领域关系的独立工具集合。本 change 仅迁移现有“获取唤取链接”和“覆盖地图”入口，不新增抽卡历史解析或导出能力。

## User-visible Behavior

### 1. Modular application shell

1. 应用启动后进入“总览”页。
2. 左侧导航至少提供：总览、成就管理、连招助手、游戏工具；设置和使用帮助固定在导航底部。
3. 总览只展示已有公共状态的摘要和快捷入口：成就统计、当前/最近选择的连招状态、游戏工具入口。总览不重建成就统计规则。
4. 成就管理页保留现有搜索、筛选、排序、状态变更、追踪浮窗、OCR、Wiki、Legacy 导入和 JSON/Excel 交换行为。
5. 游戏工具页承载现有“获取唤取链接”和“覆盖地图”，行为与错误语义保持不变。
6. 主题、检查更新和帮助入口仍可到达；现有 UI Automation ID 在行为等价控件上尽量保留，验证脚本同步到新导航结构。

### 2. Rotation profile lifecycle

1. Native 连招流程使用带 `schemaVersion` 的 JSON 文档，至少保存：名称、队伍槽位、角色别名、初始槽位、Opener、Loop 和可选相对图标引用。
2. 流程保存在当前 Native data root 下的 `rotations/`。默认 data root 继续遵循当前源码：`<程序目录>\data`，`WUWA_NATIVE_DATA_ROOT` 可覆盖测试/运行位置。
3. 连招助手提供显式文件选择，读取 wuwa-Hekili 的 `team_config`、`team_aliases`、`initial_char_index`、`opener_script` 和 `loop_script`，一次性转换为 Native 流程。
4. Hekili 导入是只读、显式、一次性的：不得修改源文件，不监视旧仓库，不建立双向同步。
5. 导入必须先完整验证后再写入。未知动作、非法角色槽位、空名称或结构损坏使整个导入失败，不留下半写入流程。
6. 旧 `custom_icon` 的绝对路径不得进入 Native 文档。可安全归一化为 Native 资源根下相对路径时保留，否则丢弃图标引用并返回非阻断 warning。
7. 用户可以在连招页面查看已保存流程、选择流程、删除流程和执行 Hekili 一次性导入。可视化连招文本编辑器不在本 change 范围。

### 3. Rotation semantics

1. 每次运行从固定逻辑 `START` 提示开始，等待配置的启动动作。
2. 启动后按顺序执行 Opener；Opener 完成后：
   - Loop 非空时进入 Loop；
   - Loop 为空时进入 Finished 状态。
3. Loop 完成后从 Loop 第一步继续循环。
4. 普通动作只有在“期待动作按下”且随后“同一逻辑动作松开”时推进一步。其他按下或松开事件不得推进。
5. Heavy 使用 Basic 的物理绑定，但只有同一动作持续达到配置阈值并松开后才推进；短按、其他动作松开和重复按下不得推进。
6. Intro 必须匹配目标角色槽位；推进后更新当前角色槽位。
7. Reset 返回 `START`/Opener 起点并清除 pending/hold 状态。
8. Reselect 停止当前运行并恢复连招页面，保留用户已保存的流程和绑定。
9. 未知、未绑定、顺序错误或重复输入只产生可诊断结果，不改变当前步骤。
10. 预览公开当前动作和后续两个动作，并正确跨越 Opener/Loop 边界。

### 4. Keyboard and mouse bindings

1. MVP 支持键盘和鼠标，不支持手柄。
2. 连招页面提供最小绑定界面：点击动作字段后按下键盘键或鼠标键完成绑定。
3. 绑定至少覆盖 Start、Reset、Reselect、Basic/Heavy、Skill、Liberation、Echo、Jump、Dodge、Execution 和 Intro 1/2/3。
4. 提供常用默认值；没有安全默认值的动作可以保持未绑定。
5. 同一物理输入不得同时绑定两个逻辑动作；存在重复或当前流程所需动作未绑定时禁止启动，并明确列出问题。
6. 绑定保存在 Native data root 的连招设置中，不写入成就 generation。
7. 固定安全停止快捷键使用 `Ctrl+Shift+F11`，不依赖用户映射；OCR 的 F12 快捷键和地图快捷键保持不变。

### 5. Runtime and overlay lifecycle

1. 启动连招前必须找到可见、未最小化且满足尺寸要求的《鸣潮》客户区；找不到时主窗口保持显示并报告错误。
2. 启动成功后主窗口隐藏，游戏获得焦点，连招提示浮窗显示在游戏客户区内。
3. 浮窗为 Topmost、ShowInTaskbar=false、NoActivate、点击穿透；运行时不得抢夺游戏键盘或鼠标焦点。
4. 浮窗显示三个槽位：当前动作、下一动作、后续动作。默认使用通用动作徽标、角色/描述和当前绑定键文字；合法相对自定义图存在时可优先显示。
5. 浮窗位置相对游戏客户区计算并跟随客户区移动/缩放；MVP 不要求运行中拖拽编辑位置。
6. 只有已验证游戏窗口处于前台时才接受动作并推进。切出游戏后自动暂停并隐藏浮窗；游戏重新前台时恢复原步骤和浮窗。
7. 游戏窗口关闭、失效或持续无法取得客户区时，运行停止，监听释放，主窗口恢复到连招页面并显示原因。
8. `Ctrl+Shift+F11` 在运行期间始终停止连招、释放 Hook、关闭浮窗并恢复连招页面。
9. 关闭应用时必须注销快捷键、停止输入源、释放 Win32 Hook 和关闭连招浮窗。

## Safety Boundary

连招助手生产路径只能消费只读输入事件。其实现及调用链不得调用：

- `SendInput`
- `mouse_event`
- `PostMessage` 输入路径
- `keybd_event`
- 游戏进程内存读取/写入或注入 API

现有 OCR 自动导航仍可在自己的边界内使用输入适配器；连招模块不得依赖或调用这些发送输入的方法。低级键鼠 Hook 必须始终把事件传给 `CallNextHookEx`，不得吞掉玩家操作。

## Architecture Decisions

### Application boundaries

- `MainWindow` 收敛为应用壳：导航、页面生命周期、主题和全局协调。
- 成就行为仍由 `AchievementWorkspace` 提供；成就视图仅调用公共命令/查询。
- 连招领域放入 `Wuwa.Core`，文件、JSON、Win32 Hook 和游戏窗口前台监视放入 `Wuwa.Infrastructure`，WPF 页面/浮窗和运行协调放入 `Wuwa.App`。
- 地图和唤取链接从主成就页面迁出，但现有 Core/Infrastructure 行为不重写。
- 本 change 不强制引入完整 MVVM 框架；允许以可测试的 View + coordinator/service 渐进收敛现有 code-behind。

### Public behavior seam

最高公共行为测试面为 `RotationRunSession` 的可观察快照与状态转换。它隐藏步骤索引、pending 键和计时实现，公开的完成条件只由行为定义：

- Start/Reset/Stop
- 接收标准化 `RotationInputEvent`
- 设置游戏前台/暂停状态
- 当前 `RotationRunSnapshot`（运行状态、当前角色、当前及后续动作、诊断结果）

生产 `WindowsRotationInputSource` 与测试 `ScriptedRotationInputSource` 消费同一输入契约；生产游戏前台监视与 fake monitor 消费同一窗口状态契约。测试只断言 session 的公共快照，不断言私有字段或内部调用次序。

### Persistence

- 成就 generation 不承载连招流程或按键映射。
- Rotation store 使用 Native data root 下独立、版本化 JSON 文件和原子临时文件替换，避免半写入。
- 发布目录只包含可选只读示例/通用徽标；用户流程不写入 `resources/`。

## Scope

### Included

- 左侧导航和总览页。
- 成就管理、连招助手、游戏工具、设置/帮助模块入口。
- 现有成就页面与工具页面的行为保持迁移。
- Native 连招模型、Hekili JSON 一次性导入、存储、解析和运行状态机。
- 键鼠默认映射和最小绑定 UI。
- 只读键鼠 Hook、游戏前台验证、三步浮窗和隐藏/恢复生命周期。
- 单元测试、UI 自动验证、测试窗口 smoke、真实游戏 smoke 说明与证据。

### Non-goals

- Xbox、PS 或其他手柄支持。
- 可视化连招文本/步骤编辑器。
- 视频或实时技能图标截取。
- Python/PySide6 运行时或 Python 子进程。
- 复制旧 wuwa-Hekili 源码或技能素材。
- 新增抽卡历史抓取、展示、JSON/Excel 导出。
- 修改成就状态、成就组、OCR 合并、Wiki 身份或 exchange 领域规则。
- 改变全局 Native data root 政策。
- 后台监视或双向同步旧 Hekili 文件。

## Error and Recovery Behavior

- Profile validation/import failure：不写文件，页面保留当前选择并显示结构化错误。
- Rotation store write failure：已有流程保持有效；临时文件不成为权威流程。
- Missing required binding：不启动、不隐藏主窗口。
- Game window discovery/focus failure：不启动、不安装运行 Hook。
- Runtime Hook failure：关闭任何已创建浮窗，恢复主窗口并报告错误。
- Game loses foreground：暂停而非重置。
- Game window disappears：停止并恢复主窗口。
- Application shutdown：best-effort 同步释放 Hook、热键、timer 和浮窗；不得留下后台监听。

## Testing Decisions

### Automated public behavior

1. `RotationRunSession` tests cover START、Opener→Loop、empty Loop→Finished、Loop wrap、Reset、Stop、foreground pause/resume and three-step preview.
2. Input tests cover exact press/release identity, wrong release, repeated down, Heavy threshold and target Intro slot.
3. Parser/import tests cover aliases、enhanced variants、unknown tokens、invalid slots、atomic failure and absolute icon stripping.
4. Store tests use temporary `WUWA_NATIVE_DATA_ROOT`/explicit root and verify versioning、round-trip、replacement failure recovery and source-file immutability.
5. Binding tests cover duplicate detection and required-action completeness.
6. Existing achievement、OCR、Wiki、exchange and persistence tests remain green.

### UI and runtime verification

1. Update UI automation to navigate Dashboard、Achievements、Rotation、Game Tools、Settings/Help and verify retained controls are reachable on their new pages.
2. Preserve tracker UI verification through the new navigation route.
3. Add an environment-gated Windows smoke using a visible test window to verify overlay bounds、NoActivate/click-through lifecycle、pause/hide and cleanup.
4. Run Release build/test and portable publish verification.
5. Final closure requires a real《鸣潮》无边框/窗口化 smoke verifying：
   - overlay stays above the game without taking focus;
   - physical keyboard/mouse actions still reach the game;
   - matching actions advance and wrong actions do not;
   - Alt-Tab pauses/hides and foreground return resumes;
   - `Ctrl+Shift+F11` stops and restores the Rotation page;
   - no automatic game action is produced.
6. If the real-game environment is unavailable, implementation may be code-complete but the change remains verification-blocked.

## Risks and Mitigations

- `MainWindow.xaml.cs` currently concentrates shell, achievement, OCR, map and system behavior. Mitigation：use expand → migrate → contract, preserve event behavior before deleting old regions, and keep UI automation IDs.
- Global low-level Hook can leak or interfere if cleanup is wrong. Mitigation：single owner, idempotent disposal, fixed stop hotkey and shutdown tests.
- Foreground/window APIs can report success without visible game behavior. Mitigation：reuse verified game window discovery/bounds facts, gate input on foreground state and require real-game smoke.
- Hekili profiles contain machine-specific absolute paths. Mitigation：never persist absolute paths; import warnings are explicit.
- Native data location knowledge is stale in `.aiknowledge`. Mitigation：implementation follows current source/README data-root behavior; evidence records the mismatch for bounded knowledge finalization.

## Rollback

The new shell and rotation module must remain separable from achievement state. Rollback consists of removing/disabling the Rotation navigation entry and restoring the prior workspace view routing; no achievement generation migration or data rewrite is required. Rotation files under `data/rotations/` are independent and may be retained without affecting older builds.
