# Spec：map-overlay-mvp

正文默认使用中文；路径、命令、配置键名保持原文。

## 意图

当前 Native WPF 应用只能在独立窗口中查看成就数据，玩家在游戏中查看 Kuro 地图时需要频繁切换窗口。此次变更为 Native 应用增加一个固定地图页面的无边框覆盖层，使玩家可以在《鸣潮》的无边框/窗口化全屏模式下直接唤出可交互地图，同时不侵入游戏进程。

## 方案摘要

在 `Wuwa.App` 中增加一个可复用的 WPF `MapOverlayWindow`，内部承载 WebView2 并导航到 Kuro 地图初始入口 `https://www.kurobbs.com/mc/map/`。主窗口通过现有的 `HwndSource` 消息钩子注册全局快捷键，优先使用 `Alt+M`，冲突时回退到 `Ctrl+Alt+M`；覆盖层通过 `Wuwa.Infrastructure` 获取游戏窗口客户区的屏幕坐标和尺寸，显示时覆盖该客户区，游戏失焦或最小化时自动隐藏。

## 范围 / 非目标

### 范围内

- 在主界面提供打开游戏地图的按钮，并显示当前生效的快捷键。
- 使用 WebView2 加载 Kuro 地图初始入口 `https://www.kurobbs.com/mc/map/`，不固化任意用户的坐标、缩放或 `items` 标记筛选。
- 创建无边框、无任务栏按钮、置顶的地图覆盖窗口，覆盖《鸣潮》游戏客户区。
- 支持地图原有的鼠标拖动、滚轮缩放和点击交互。
- 使用 `RegisterHotKey` 优先注册 `Alt+M`，注册失败时尝试 `Ctrl+Alt+M`；再次按生效快捷键或按 `Esc` 隐藏地图。
- 复用并扩展现有游戏窗口查找/Win32 适配能力，处理窗口移动、尺寸变化、DPI 和多显示器坐标。
- 游戏窗口最小化、不可见或不再是前台窗口时自动隐藏覆盖层；地图 WebView2 实例和页面状态在隐藏期间保留。
- 在 WebView2 Runtime 不存在、游戏窗口不可用或无法注册快捷键时给出明确提示，并保留主界面按钮作为入口。

### 范围外

- 不支持真正的 DirectX 独占全屏覆盖；不做 DLL 注入、DirectX Hook 或游戏进程内绘制。
- 不使用低级键盘钩子主动吞掉输入，不保证游戏使用 Raw Input 或忽略修饰键时完全看不到 `Alt+M` 中的 `M`。
- 不做地图地址编辑、自定义快捷键、多个地图实例、点击穿透或地图与游戏同时接收鼠标输入。
- 不建立地图标记与 `AchievementWorkspace` 成就状态之间的同步，也不改变成就身份、状态转换或 generation 持久化语义。
- 不把 legacy 文件、地图缓存或 WebView2 页面状态写入 Native 工作区数据。

## 可观察行为

1. **地图入口**：当应用已启动且用户点击“打开游戏地图”时，系统必须查找可见且未最小化的《鸣潮》窗口；找到后显示地图覆盖层，找不到时不显示空白覆盖层并提示原因。
2. **覆盖范围**：地图显示后，覆盖层必须与游戏客户区的屏幕位置和宽高一致；游戏移动、改变窗口大小或发生显示器/DPI 变化后，覆盖层必须跟随更新。
3. **地图交互**：地图显示后，WebView2 必须允许页面自身支持的拖动、滚轮缩放和点击操作；覆盖层隐藏后，游戏窗口可以重新接收输入。
4. **快捷键显示与切换**：系统必须在应用后台运行时响应已注册的全局组合键。优先生效 `Alt+M`；若该组合键注册失败，必须尝试 `Ctrl+Alt+M` 并向用户展示实际生效的组合键。生效组合键必须在地图隐藏时显示地图，在地图显示时隐藏地图。
5. **关闭与失焦**：地图显示时按 `Esc` 必须隐藏地图。游戏最小化、不可见或失去前台时必须自动隐藏地图，并且不能继续覆盖其他应用；地图重新显示时必须复用已有页面状态。
6. **显示模式边界**：当无法取得有效的游戏客户区 HWND/屏幕边界，系统必须不报告覆盖成功，并提示用户将游戏切换到无边框/窗口化全屏；不得尝试注入游戏进程。
7. **运行时依赖**：目标机没有可用 WebView2 Runtime 时，系统必须在打开地图时给出可行动的依赖提示，而不是让应用启动失败或显示不可用的地图窗口。
8. **应用退出**：主应用关闭时必须释放全局快捷键、覆盖层窗口和 WebView2 资源，不留下置顶窗口或悬挂的窗口消息处理。

## First-Principles Snapshot

- 真实目标：在游戏内快速查看可交互 Kuro 地图，减少 Alt+Tab 切换。
- 最小机制：独立 WPF/WebView2 顶层窗口 + 游戏客户区定位 + 全局组合键切换。
- 边界 / 非目标：只覆盖无边框/窗口化全屏，不进入游戏渲染链路，不做低级键盘拦截。
- 当前事实：项目是 Native WPF/.NET 8；`MainWindow` 已有 `HwndSource`/`RegisterHotKey`；`WindowsGameWindowCapture` 已有游戏窗口枚举和 Win32 坐标转换基础。
- 关键未知：目标电脑的 WebView2 Runtime 安装情况、不同游戏权限等级下的前台/快捷键行为、不同 DPI/显示器组合下的坐标精度。
- 证据门槛：`dotnet test`、`dotnet build` 通过，并在真实 Windows 游戏窗口上完成显示、交互、失焦和回收验收。
- 推荐选择：使用地图初始入口、`Alt+M` 失败回退 `Ctrl+Alt+M`、游戏失焦自动隐藏；以可逆的外部覆盖层替代高风险注入方案。

## 受影响文件 / 模块

| 路径 / 模块 | 预期动作 | 作用 / 链路 |
|---|---|---|
| `src/Wuwa.App/Wuwa.App.csproj` | modify | 添加 WebView2 依赖，并确保构建/发布能够检测运行时缺失。 |
| `src/Wuwa.App/MapOverlayWindow.xaml` | add | 定义无边框地图窗口和 WebView2 容器。 |
| `src/Wuwa.App/MapOverlayWindow.xaml.cs` | add | 管理 WebView2 初始化、固定 URL、显示隐藏、Esc、边界更新和资源释放。 |
| `src/Wuwa.App/MainWindow.xaml` | modify | 增加“打开游戏地图”入口和快捷键状态提示。 |
| `src/Wuwa.App/MainWindow.xaml.cs` | modify | 注册/注销地图全局快捷键、处理 `WM_HOTKEY`、协调游戏窗口查找和覆盖层生命周期。 |
| `src/Wuwa.Infrastructure/WindowsGameWindowCapture.cs` | modify | 暴露可复用的游戏客户区屏幕边界/窗口状态适配，不把 Win32 定位逻辑复制到 WPF。 |
| `tests/Wuwa.Tests/` | modify if needed | 保留现有回归测试；仅为可独立验证的窗口边界适配增加测试，不测试 WebView2 私有实现。 |
| `scripts/publish-native.ps1` / 发布说明 | modify if needed | 若发布验证发现需要额外 Runtime 提示或资产说明，补充 Native 发布路径的依赖说明。 |

## 领域语言

- **Native 应用实现**：本功能属于现有 Native WPF/.NET 8 Windows 运行时；地图覆盖层是界面和 Infrastructure 的适配能力，不绕过 `AchievementWorkspace` 领域入口。
- **游戏窗口客户区**：覆盖层对齐的是游戏 HWND 的可绘制客户区，而不是带标题栏的外框或整个桌面。
- **地图覆盖层**：本 change 新增的 UI 名称，表示独立的、可交互的 WebView2 顶层窗口，不表示游戏进程内 Overlay。

## 设计 / 决策

### 关键决策

1. **使用独立 WPF 顶层窗口承载 WebView2**：WebView2 是成熟的 Windows 浏览器承载方式，能直接支持地图的网页交互；独立窗口可以复用现有 WPF 生命周期，同时不需要把游戏窗口改造成子窗口。
2. **复用现有 Win32 游戏窗口适配边界**：客户区坐标、前台检测和窗口状态继续由 `Wuwa.Infrastructure` 负责，WPF 只消费结构化结果，避免复制 `EnumWindows`/DPI/坐标转换代码。
3. **使用现有 `HwndSource` 注册全局组合键**：`MainWindow` 已有 `RegisterHotKey` 和 `WM_HOTKEY` 处理路径；地图只增加独立 ID，并在冲突时回退 `Ctrl+Alt+M`。不增加低级键盘钩子，以降低对游戏输入和安全软件的侵入。
4. **使用地图初始入口，暂不固化用户 URL 状态**：MVP 只验证地图覆盖能力，地图的坐标、缩放和标记筛选属于用户/网站状态，不写入 generation；后续如需导入用户链接再单独设计设置契约。
5. **前台和失焦策略优先保护桌面**：覆盖层只在游戏有效且处于前台时保持显示，游戏最小化或切出时自动隐藏；这牺牲了切出后继续看地图的便利性，换取不会遮挡其他应用的可预测行为。
6. **以无边框/窗口化全屏为支持边界**：普通 WPF 顶层窗口无法可靠覆盖独占全屏；MVP 对无有效客户区边界的情况失败并提示，不引入 DirectX 注入风险。

### 不采用的方案

- **DirectX Overlay / DLL 注入**：可覆盖独占全屏，但会引入反作弊、版本兼容、崩溃和发布风险，超出 MVP。
- **低级键盘钩子**：可以更强地尝试屏蔽组合键，但会影响全局输入并增加权限/安全边界；MVP 使用 `RegisterHotKey` 和低冲突回退键。
- **浏览器外部进程或系统默认浏览器**：无法可靠对齐客户区，也无法实现同一窗口的自动隐藏和状态复用。
- **点击穿透窗口**：地图必须支持拖动、缩放和标记点击，点击穿透会直接破坏核心交互。

## 验证计划

- `dotnet test WutheringWavesAchievement.sln -c Release`，预期：现有 Core/Infrastructure 回归测试全部通过。
- `dotnet build WutheringWavesAchievement.sln -c Release`，预期：WPF 项目成功还原 WebView2 依赖并生成可启动 Native 应用。
- `powershell -ExecutionPolicy Bypass -File scripts/publish-native.ps1 -Configuration Release`，预期：发布流程不因地图依赖或现有 OCR 资产复制而失败。
- Windows 真实运行验收：以无边框/窗口化全屏运行《鸣潮》，通过按钮和 `Alt+M`/回退快捷键打开地图，验证覆盖边界、拖动缩放、移动/调整窗口、DPI、多显示器、Alt+Tab、最小化、Esc、再次打开和应用退出回收；预期：所有行为符合本文件“可观察行为”，独占全屏和缺失 WebView2 时给出明确失败提示。

## 风险 / 回退

- **独占全屏无法覆盖**：接受为已确认边界；检测不到有效客户区时不显示覆盖层，提示切换无边框模式。
- **WebView2 Runtime 缺失**：打开地图前检测并提示安装/修复 Runtime；不让初始化异常终止主应用。
- **快捷键被其他程序占用或权限等级不同**：按 `Alt+M` → `Ctrl+Alt+M` 顺序注册，失败时保留按钮入口并显示原因；不提升应用权限，也不注入游戏。
- **DPI/多显示器坐标偏移**：统一在 Infrastructure 侧以屏幕像素获取客户区，再在 WPF 边界转换为设备无关单位；真实 Windows 验收覆盖 100%/125%/150% DPI。
- **地图覆盖层残留置顶**：主窗口关闭、游戏失焦和覆盖层关闭路径都必须集中释放窗口与快捷键；出现回归时可整体移除地图窗口和包引用，不涉及 Core 数据迁移。
