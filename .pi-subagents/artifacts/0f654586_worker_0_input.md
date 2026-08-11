# Task for worker

Implement OPSX Task 01 only. You are the sole implementation writer. Read openspec/changes/native-achievement-workspace/spec.md, tasks.md, evidence.md, and tasks/01-native-workspace-tracer.md before editing.

Write authority: only native/**. Do not edit OPSX artifacts, Python files, resources, README, git state, or the user-deleted legacy openspec paths. Do not stage or commit.

Build a VS2022-compatible net8.0-windows WPF vertical slice with WutheringWavesAchievement.sln, global.json, Wuwa.Core, Wuwa.Infrastructure, Wuwa.App, and Wuwa.Tests. Use a red-capable public AchievementWorkspace behavior seam. Implement immutable AchievementId derived deterministically from LegacyCode, the four progress statuses, query/filter/sort behavior, same-revision statistics, and mutual-exclusion group transitions. Define IAppDataStore and InMemoryAppDataStore; add a read-only shipped JSON adapter so the WPF app loads the existing 958-row resources through the workspace rather than reading JSON in UI code.

The WPF app must launch into a usable dark charcoal/teal workspace with left navigation, dense filter controls, metrics, and a virtualized achievement DataGrid. It must load all shipped rows, allow explicit status changes, and update filtered rows/metrics. Keep visual work sufficient for the tracer but do not implement Task 05 polish, theme persistence, Wiki, import/export, OCR, overlay, migration, or transactional generations.

Use TDD for workspace behavior: observe focused tests fail for the missing seam before implementing, then make them green. Include synthetic 2/3-member groups because real data has none. Expectations come from the spec, not private implementation details. Avoid WPF dependencies in Wuwa.Core.

Validation: dotnet test, dotnet build -c Release, and a bounded WPF launch smoke against shipped resources. Report changed files, behavior delivered, red/green evidence, exact commands and exit codes, validation output, decisions, and residual risks. Stop and report a blocker if the spec requires an unapproved product decision.

## Acceptance Contract
Acceptance level: checked
Completion is not accepted from prose alone. End with a structured acceptance report.

Criteria:
- criterion-1: Implement the requested change without widening scope
- criterion-2: Return evidence sufficient for an independent acceptance review

Required evidence: changed-files, tests-added, commands-run, residual-risks, no-staged-files, validation-output, diff-summary

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