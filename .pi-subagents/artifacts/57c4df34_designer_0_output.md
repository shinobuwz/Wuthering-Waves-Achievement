**Review Findings**
- **High:** [core/achievement_table.py:173](E:/gitlab/Wuthering-Waves-Achievement/core/achievement_table.py:173) disables focus and selection, while [core/achievement_table.py:422](E:/gitlab/Wuthering-Waves-Achievement/core/achievement_table.py:422) makes status changes mouse-only, including a one-second long press. The WPF version needs focusable rows and explicit `Set completed`, `Set unavailable`, and `Reset` commands with keyboard bindings.
- **High:** Achievement progress is keyed by `编号` ([core/config.py:370](E:/gitlab/Wuthering-Waves-Achievement/core/config.py:370)), but category changes can regenerate those identifiers ([core/config.py:528](E:/gitlab/Wuthering-Waves-Achievement/core/config.py:528)). Tracked achievements must use an immutable internal identity or participate in the same transactional ID migration.
- **High:** OCR minimizes and later restores the main window ([core/ocr_tab.py:210](E:/gitlab/Wuthering-Waves-Achievement/core/ocr_tab.py:210)). A WPF overlay owned by `MainWindow` would disappear with it. Make the overlay ownerless and manage it through an application-level `OverlayCoordinator`; explicitly enter `SuspendedForCapture` during OCR.
- **Medium:** The frameless shell and hand-built caption controls ([core/main_window.py:22](E:/gitlab/Wuthering-Waves-Achievement/core/main_window.py:22), [core/custom_title_bar.py:55](E:/gitlab/Wuthering-Waves-Achievement/core/custom_title_bar.py:55)) should not be ported wholesale. Use native Windows chrome for Snap Layouts, system menus, keyboard movement, and assistive technology. Reserve custom chrome for the compact overlay.
- **Medium:** Fixed minimum/fixed sizes, including the 1080x700 shell and 850x600 settings dialog ([core/main_window.py:51](E:/gitlab/Wuthering-Waves-Achievement/core/main_window.py:51), [core/settings_dialog.py:25](E:/gitlab/Wuthering-Waves-Achievement/core/settings_dialog.py:25)), are unsuitable for text scaling and smaller work areas.
- **Medium:** Statistics are custom-painted and expose details only through pointer hover ([core/statistics_tab.py:18](E:/gitlab/Wuthering-Waves-Achievement/core/statistics_tab.py:18)). Every chart needs an equivalent accessible summary/table and keyboard-readable values.
- **Medium:** Settings and help methods exist, but the current shell exposes only theme/minimize/maximize/close controls; `ManageTab.open_settings` is not wired into the visible navigation ([core/manage_tab.py:526](E:/gitlab/Wuthering-Waves-Achievement/core/manage_tab.py:526)). Add persistent profile, settings, and help entries to the shell.
- **Low:** Most semantic tokens have good contrast, but light `TEXT_GRAY` on `APP_BG` is only **3.99:1** ([core/styles.py:35](E:/gitlab/Wuthering-Waves-Achievement/core/styles.py:35)); do not reuse that pairing for normal-sized text.

**Recommended WPF Experience**
- Retain four primary work areas: **Achievements**, **Tracked**, **Insights**, and **Data & Scan**. Put profile switching, Settings, Help, theme, and update status in a persistent shell footer/header.
- Make **Achievements** the default: virtualized `DataGrid`, search, version/category/state filters, compact metrics, explicit status menu, and a pin-style Track toggle. Preserve `未完成`, `已完成`, `暂不可获取`, and mutually exclusive `已占用` semantics.
- Consolidate duplicate import/export controls under **Data & Scan**. Sync should be `fetch -> diff preview -> commit`; OCR should expose `NotFound`, `WrongResolution`, `Ready`, `LoadingModel`, `Scanning`, `Stopping`, `Review`, `Saving`, and `Error`.
- The **Tracked** view owns ordering, untracking, overlay visibility, monitor/anchor choice, opacity, and compact/expanded presentation. Completion should not silently untrack an achievement.
- Overlay states: `Hidden`, `Empty`, `Visible`, `Collapsed`, `LockedClickThrough`, `SuspendedForCapture`, and `Error/StaleReference`. Show up to six ordered rows plus `+N more`; avoid scrolling while click-through is active.
- Overlay header actions: drag handle, collapse/expand, lock/unlock, and hide. Rows expose status, short description, complete/reset, and untrack only while unlocked. A notification-area menu must always provide Show, Hide, Unlock, and Exit recovery actions.
- Use an ownerless `Topmost`, `ShowInTaskbar=false` tool window. Apply `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` only in locked mode. Keep the process alive explicitly when the main window is hidden, and shut down through the tray/application command.
- Default the overlay to the game monitor’s top-right work-area anchor with a safe inset. Persist logical DIPs, monitor identity, anchor, size, and offset, then clamp on monitor removal.

**Visual Architecture**
- Use `.NET 8` WPF with `Domain`, `Application`, `Infrastructure`, and `Presentation` projects. MVVM view models depend on repositories/services, never HWND, JSON, OCR, or dialog APIs directly.
- Store bundled achievement data read-only; migrate profiles, progress, tracked order, and settings to `%LocalAppData%`. Prefer SQLite with schema migrations and atomic transactions, retaining JSON/Excel as interchange formats.
- Introduce immutable `AchievementId`; retain the current category-derived number as display/import metadata. Tables should include `Achievements`, `Profiles`, `Progress`, `TrackedAchievements(Order)`, and `OverlaySettings`.
- Isolate OCR behind `IOcrScanService`. For the first cutover, the existing Python/OpenCV/ONNX engine can run out of process behind a versioned JSON protocol; a C# engine rewrite should be a separate project.
- Build theme dictionaries for semantic brushes, typography, spacing, control states, charts, and high contrast. Preserve the restrained neutral/emerald identity from [core/styles.py](E:/gitlab/Wuthering-Waves-Achievement/core/styles.py), while using `Segoe UI Variable` with `Microsoft YaHei UI` fallback.

**Accessibility, DPI, And Monitors**
- Declare Per-Monitor-V2 awareness, handle `WM_DPICHANGED`, negative virtual-screen coordinates, mixed-DPI moves, work-area changes, taskbar relocation, and disconnected displays.
- Avoid fixed row/control heights; verify 100-225% DPI and 100-200% Windows text size. Use text wrapping/trimming with accessible full names.
- Provide visible focus, logical tab order, AutomationProperties names/help text, screen-reader status announcements, and keyboard alternatives for every pointer action. State and OCR mismatch indicators must use icon/text as well as color.
- Honor Windows High Contrast and reduced-animation settings. Do not animate overlay position or opacity when client-area animation is disabled.

**First-Cutover Non-Goals**
- Pixel-for-pixel Qt parity, custom main-window chrome, custom backgrounds, avatar/character artwork polish, and chart animation.
- Editing category order, authoring mutually exclusive groups, or deleting whole versions; preserve and display existing semantics first.
- Rewriting OCR recognition, supporting arbitrary game resolutions, cloud sync, accounts, or non-Windows platforms.
- Guaranteeing overlay visibility over exclusive fullscreen or protected/anti-cheat surfaces; document borderless-windowed mode as supported.

**Verification Scenarios**
1. Migrate several profiles with all status values and verify counts, group exclusions, IDs, and tracked order.
2. Complete every primary flow using keyboard only and inspect with Narrator.
3. Test shell and overlay at 100/125/150/200/225% DPI and 200% text size.
4. Move both windows between mixed-DPI monitors with negative coordinates; unplug the overlay monitor and verify clamping.
5. Minimize/close the shell while the overlay remains usable; recover locked click-through mode from the tray.
6. Switch profiles while overlayed and verify immediate, atomic content replacement without cross-profile progress leakage.
7. Start/cancel/fail OCR and verify the overlay suspends before capture and restores its prior mode and position.
8. Exercise empty data, zero tracked items, missing tracked IDs, corrupt migration input, network failure, and unsaved OCR results.
9. Verify light, dark, High Contrast, reduced motion, taskbar relocation, sleep/resume, and Explorer restart.

**Residual Risks**
- This was a static code and asset assessment; no running Qt screenshots, game process, OCR capture, Narrator session, or WPF prototype was available.
- Exclusive-fullscreen/topmost behavior and anti-cheat compatibility require testing against the actual game.
- Shipping the existing Python OCR worker reduces cutover risk but adds deployment, process supervision, and protocol-versioning work.
- The worktree contains pre-existing deleted OpenSpec files and untracked `.pi-subagents/`; none were modified.