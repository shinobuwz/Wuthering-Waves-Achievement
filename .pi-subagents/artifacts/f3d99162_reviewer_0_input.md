# Task for reviewer

Read-only independent high-risk plan review. Do not edit any project or OPSX files. Inspect the repository directly and review the complete draft below. This is the exactly-one mandatory fresh reviewer execution for OPSX Plan.

User objective: replace the Python/PySide6 application with a refined Windows-only VS2022 application; eventually remove Python/Qt; use C# WPF for desktop UI and a project-specific C++ PP-OCRv5 DLL using ONNX Runtime/OpenCV/Clipper2; add an always-on-top tracked-achievement overlay where completing an item removes it.

Proposed serial change split:
1. native-achievement-workspace: side-by-side .NET 10 WPF app covering achievement management, statistics, anonymous Wiki sync, JSON/Excel import/export, theme and update check. No OCR or overlay yet. Existing Python app remains available.
2. native-ocr-scan: native C++ PP-OCRv5 engine plus game capture, single-page/global scan, cancellation, result preview, non-downgrade progress merge.
3. tracked-achievement-overlay: persistent tracked set, independent transparent topmost WPF window, edit/locked click-through modes, global shortcut, multi-monitor/DPI behavior; completing an item untracks it after a brief transition.
4. native-cutover-release: release packaging, legacy cleanup/removal, docs, final data migration and replacement of Python entry point after parity verification.

Change 1 behavioral draft:
- Windows x64, VS2022, net10.0-windows WPF. Custom dark charcoal/teal game-tool visual system; dense virtualized achievement table; no marketing screen.
- On launch load the shipped base_achievements.json/category_config.json and the selected legacy profile when available. Preserve Chinese JSON field names and statuses: 未完成, 已完成, 暂不可获取, 已占用. Preserve achievement-group mutual exclusion semantics.
- Native mutable state lives under %LocalAppData%/WutheringWavesAchievement. One-time migration reads legacy resources/config.json and resources/user_progress_{uid}.json without modifying them, writes atomically, and records completion only after successful validation. Existing files remain recoverable.
- Single local profile in native UI; legacy current user/UID is selected for migration. Multi-user administration, avatar/character artwork management, category/group editing and OCR are non-goals for change 1.
- Users can search name/description; filter version, first/second category, hidden/obtainable state; hide completed; sort default/incomplete-first; change status; observe live statistics with achievement groups counted once.
- Anonymous Wiki sync uses the existing endpoint and stable cache semantics, adds/updates base data without downgrading or deleting user progress on network/parse failures.
- JSON and Excel import/export preserve supported legacy columns and status formats; destructive import requires confirmation and a backup/transaction boundary.
- Theme choice and secure GitHub update check are retained. No TLS verification bypass.

Architecture/testing draft:
- Solution projects Wuwa.App, Wuwa.Core, Wuwa.Infrastructure, Wuwa.Tests. WPF UI consumes one deep public seam, AchievementWorkspace, for load/query/status/statistics operations.
- IAppDataStore is the system-boundary adapter with JsonAppDataStore production and InMemoryAppDataStore test implementations, proving the seam is replaceable. UI does not access JSON directly.
- Highest behavior seam: AchievementWorkspace public observable behavior, tested against temp legacy fixtures and both adapters; UI smoke checks cover launch, 958-row load, status update and filter/stat refresh.
- Verification: dotnet test; dotnet build Release; migration roundtrip on temp directories; live anonymous Wiki probe isolated from user data; WPF launch/UI Automation and screenshots at 1080x700 and 1440x900; package smoke launch.

Proposed Change 1 vertical tasks:
1. Native tracer: launch WPF, load existing 958-row library through AchievementWorkspace, filter and change status, show live metrics with tests.
2. Legacy migration and atomic native persistence with recovery tests.
3. Anonymous Wiki sync through workspace with isolated live/cache verification.
4. JSON/Excel import-export roundtrip and destructive-operation backup behavior.
5. Refined WPF views, theme, secure update check, keyboard/DPI/accessibility and screenshot review.
6. Side-by-side release publish and documentation, without deleting Python.

Known risks: data schema migration, multi-module rewrite, status/group semantics, Wiki response parsing, WPF UI automation, and later native ABI boundary. Existing worktree has unrelated user deletions under openspec/; they must not be reverted or treated as this change.

Return: (1) verdict, (2) CRITICAL/WARNING/SUGGESTION findings with repository evidence, (3) whether four changes and Change 1 scope are coherent, (4) whether the proposed public seam passes completion/deletion tests and has two adapters, (5) missing verification/blockers, and (6) precise recommended dispositions. No edits.

## Acceptance Contract
Acceptance level: checked
Completion is not accepted from prose alone. End with a structured acceptance report.

Criteria:
- criterion-1: Implement the requested change without widening scope
- criterion-2: Return evidence sufficient for an independent acceptance review

Required evidence: changed-files, tests-added, commands-run, residual-risks, no-staged-files

Review gate: required by reviewer.

Finish with a fenced JSON block tagged `acceptance-report` in this shape:
Use empty arrays when no items apply; array fields contain strings unless object entries are shown.
`criteriaSatisfied[].status` must be exactly one of: satisfied, not-satisfied, not-applicable.
`commandsRun[].result` must be exactly one of: passed, failed, not-run.
`manualNotes` and `notes` are optional strings; an empty string means no note and does not satisfy `manual-notes` evidence.
```acceptance-report
{
  "criteriaSatisfied": [
    {
      "id": "criterion-1",
      "status": "satisfied",
      "evidence": "specific proof"
    },
    {
      "id": "criterion-2",
      "status": "satisfied",
      "evidence": "specific proof"
    }
  ],
  "changedFiles": [
    "src/file.ts"
  ],
  "testsAddedOrUpdated": [
    "test/file.test.ts"
  ],
  "commandsRun": [
    {
      "command": "command",
      "result": "passed",
      "summary": "short result"
    }
  ],
  "validationOutput": [
    "validation output or concise summary"
  ],
  "residualRisks": [
    "none"
  ],
  "noStagedFiles": true,
  "diffSummary": "short description of the diff",
  "reviewFindings": [
    "blocker: file.ts:12 - issue found, or no blockers"
  ],
  "manualNotes": "anything else the parent should know"
}
```