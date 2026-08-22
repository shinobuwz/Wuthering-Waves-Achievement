# Evidence：map-overlay-mvp

正文默认使用中文；路径、命令保持原文。

`evidence.md` 是稀疏决策/失败日志加一份当前最终验证摘要。仅当事件改变了 spec/design/scope/tasks、证伪了关键假设、导致回退或方案替换、或产生改变后续推理的结论时才记录。

## Final Verification

<!-- 当前尚未进入 implement/verify；由 opsx-verify 在真实实现验证后写入并替换本节。 -->

## Decisions

### 支持边界 | plan

背景：用户希望地图覆盖游戏并支持全屏；普通 WPF/WebView2 顶层窗口无法可靠覆盖 DirectX 独占全屏。

事件：用户确认最小 MVP 只支持《鸣潮》的无边框窗口/窗口化全屏。

结论：不做 DirectX Hook、DLL 注入或游戏进程内 Overlay；独占全屏作为明确非目标，检测到无法取得有效客户区时提示用户切换显示模式。

### 快捷键策略 | plan

背景：用户希望使用 `Alt+M` 唤出地图，同时避免游戏内 M 键干扰。

事件：用户确认优先注册 `Alt+M`，冲突时回退 `Ctrl+Alt+M`，不使用低级键盘钩子。

结论：MVP 使用现有 `RegisterHotKey`/`WM_HOTKEY` 路径；只保证组合键入口和低侵入行为，不承诺拦截游戏 Raw Input。

### 地图入口状态 | implement

背景：用户提供的完整链接包含个人的坐标、缩放和 `items` 标记筛选；不同用户的地图状态不同，固化该链接会导致部分用户加载异常。

事件：用户确认覆盖层应只使用 Kuro 地图初始入口，不携带个人 URL 参数。

结论：地图 URL 改为 `https://www.kurobbs.com/mc/map/`，由地图网站自行决定默认状态；不再把用户的 `x`、`y`、`zoom` 或 `items` 写入应用。

### 失焦策略 | plan

背景：置顶窗口如果在游戏切出后继续显示，可能覆盖浏览器或其他桌面应用。

事件：用户确认游戏最小化、切出或失去前台时自动隐藏地图。

结论：覆盖层只跟随有效前台游戏窗口显示；隐藏时保留 WebView2 页面状态，回到游戏后可再次显示。

## Failures / Rollbacks

<!-- 当前没有改变方案方向的失败或回退。 -->
