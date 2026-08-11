# 04 — JSON and Excel exchange

**What to build:** Implement candidate-generation import and deterministic export for the supported progress JSON, full JSON, and 12-column Excel shapes. Preserve Unicode, group metadata, hidden flags, statuses, identity compatibility fields, and rollback behavior.

**Blocked by:** 02

**Suggested Files:** `native/src/Wuwa.Infrastructure/Exchange/`, `native/src/Wuwa.App/Views/`, `native/tests/Wuwa.Tests/Exchange/`, `native/tests/Wuwa.Tests/Fixtures/Exchange/`

**Behavior Context:**

- JSON aliases are explicitly mapped; no heuristic based on key length determines document type.
- Excel may contain one information row before its header. `名称` and `第二分类` are required; all imported status/group references are validated before activation.
- Unknown categories, duplicate identity, invalid status, and broken mutual-exclusion references are errors, not silent defaults.
- A destructive import requires confirmation at the UI and retains the prior generation.

**Acceptance:** Every accepted fixture imports and re-exports without behavioral field loss; rejected fixtures return structured diagnostics and leave the active workspace revision unchanged.

**Verification:** Golden round-trip fixtures for each shape, synthetic groups, legacy aliases, Chinese punctuation/Unicode, invalid rows, cancellation, and rollback.
