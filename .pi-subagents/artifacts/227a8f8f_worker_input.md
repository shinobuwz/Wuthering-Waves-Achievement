# Task for worker

You are reviving a previous subagent conversation.

Original run: a9162c1a-e104-4009-9295-fe40be297d80
Original agent: worker
Original session file: C:\Users\N22116\.pi\agent\sessions\--E--gitlab-Wuthering-Waves-Achievement--\2026-08-11T06-42-09-222Z_019fef8e-9ec6-727f-87b9-f8d1e56b4616\0f654586\run-0\session.jsonl

Use the stored session context as background. Answer the orchestrator's follow-up below. Do not assume the original child process is still alive.

Follow-up:
Continue the same Task 01 implementation from the current native/** files after the transient 502. Keep sole write authority to native/**. Inspect what was persisted, remove template Class1/UnitTest artifacts if obsolete, finish core/store/shipped adapter/WPF tracer/tests, run dotnet test, Release build, 958-row validation, and bounded launch smoke. Do not edit OPSX artifacts or any non-native path; do not stage or commit. Return the originally requested evidence.

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