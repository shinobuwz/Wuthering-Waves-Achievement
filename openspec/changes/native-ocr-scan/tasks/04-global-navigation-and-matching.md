# 04 — Native full-scan navigation and matching

**What to build:** Add a second Native OCR command, `OCR 全量扫描所有分类`, while preserving `OCR 自动扫描当前分类`. The command must reproduce the Python `scan_all_tabs()` flow: traverse the four primary categories in the Python-defined order, discover and visit every secondary category, scan every visible achievement page in each secondary category, merge candidates without downgrading completed status, and present one cancellable preview before any workspace mutation.

**Blocked by:** 03 — Native scan service. **Environment prerequisite:** current-category template OCR and game input require the game and tool to run at the same integrity level.

**Suggested Files:**
- `native/src/Wuwa.App/MainWindow.xaml(.cs)`
- `native/src/Wuwa.Core/OcrScanContracts.cs`
- `native/src/Wuwa.Core/OcrMatching.cs`
- `native/src/Wuwa.Infrastructure/WindowsGameWindowCapture.cs`
- `native/src/Wuwa.Infrastructure/NativeOcrClient.cs`
- `native/src/Wuwa.Infrastructure/NativeOcrTextReader.cs`
- `native/src/Wuwa.Infrastructure/NativeOcrTemplateTextReader.cs`
- `native/tests/Wuwa.Tests/`
- `core/achievement_ocr.py` (read-only parity reference)
- `resources/category_config.json`

## Behavior Context

- The existing current-category command remains unchanged and continues to scan only the currently selected secondary category.
- The new full-scan command follows Python's `PRIMARY_TAB_NAMES`, primary-tab coordinate mapping, secondary-tab matching, and scroll constants rather than introducing a new category or group data model.
- Primary-tab selection is verified by OCR after each click. A failed selection is retried using the Python retry budget; a category that still cannot be verified is reported and does not invalidate results already collected from other categories.
- Secondary tabs are matched against the ordered category names from the shipped achievement/category data. Each visible unvisited tab is clicked once. When no new tab is discovered, secondary navigation stops after the Python-equivalent bounded retry count and reports any unvisited names.
- Before scanning a selected secondary tab, the scanner waits for the list to load, then reuses the current-category page loop and the same privilege-sensitive input adapter. Repeated or empty pages stop that secondary scan without spinning indefinitely.
- Full-scan progress reports primary name, secondary name, visited/total secondary tabs, page number, matched count, and recoverable navigation/recognition warnings. Cancellation stops input and inference promptly.
- All candidates are merged in memory. A completed observation wins over an incomplete observation for the same achievement; no workspace generation changes until the single final preview is explicitly accepted.
- A failed or skipped tab is visible in the preview/error summary. It does not silently appear as a confirmed incomplete achievement.

**Acceptance:**

1. The WPF application exposes both current-category and full-scan commands without adding a new top-level application tab.
2. Given a game window at the supported resolution and matching integrity level, full scan visits every verified primary/secondary category that Python would visit, with no duplicate tab visits and bounded termination when navigation stops producing new tabs.
3. Each visited secondary category scans its achievement list until repeated/empty-page termination, and the result includes achievements from more than one secondary category in a run.
4. Progress text and cancellation remain usable while the game is minimized/foregrounded for scanning; cancelled or failed runs leave the current workspace revision unchanged.
5. Accepted full-scan results use the existing preview and `AchievementWorkspace` merge path, preserve completed-status downgrade protection, and do not alter Python files or legacy progress files.
6. Native navigation OCR uses the existing detector/recognizer path for tab discovery, while achievement rows continue to use the template-matching path; both paths share the same managed scan orchestration and error boundary.

**Verification:**

- Unit tests for ordered primary/secondary traversal, duplicate-tab suppression, bounded no-new-tab termination, cancellation, per-tab failure reporting, and completed-over-incomplete merge behavior using a fake game-navigation/capture adapter.
- A managed integration smoke that loads detector/classifier/recognizer assets and confirms tab OCR results cross the existing C ABI without exceptions.
- A manual Windows smoke with the game and tool at the same integrity level: run the full command from a category with at least two secondary tabs, confirm the game visibly changes primary/secondary selection and list position, and confirm the final preview contains rows from multiple tabs.
- `dotnet test native/WutheringWavesAchievement.sln -c Release --no-restore`, native OCR CTest, and `dotnet build native/WutheringWavesAchievement.sln -c Release --no-restore`.

**Residual risks:** Fullscreen/Raw Input changes or game UI layout changes can invalidate coordinate automation even when the Win32 API reports success; the smoke must record the native OCR log and require visible category/list changes rather than treating an accepted input API call as proof of control.
