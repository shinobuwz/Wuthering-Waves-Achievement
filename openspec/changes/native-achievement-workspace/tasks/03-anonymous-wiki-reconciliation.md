# 03 — Anonymous Wiki reconciliation

**What to build:** Port the public Wiki client and HTML-table parser, retain table/row source references, validate responses, and reconcile accepted rows through `AchievementWorkspace` into a new generation without changing existing progress identity.

**Blocked by:** 02

**Suggested Files:** `src/Wuwa.Infrastructure/Wiki/`, `src/Wuwa.Core/Sync/`, `tests/Wuwa.Tests/Wiki/`

**Behavior Context:**

- Require successful HTTP and business statuses, expected module/table schema, unique source-row references, and a plausible row count.
- Match exact `WikiSourceRef`, then exact unique full signature; use name/description fallback only during legacy bootstrap.
- Rename/category/description changes retain identity only when source reference or an unambiguous reconciliation rule proves the match.
- Ambiguous rows and remote removals never delete progress; removals become tombstones.
- Ignore request-level `traceId` and browse count in content equality.

**Acceptance:** Valid remote changes retain existing `AchievementId` and progress, add new rows, and tombstone removals; malformed, partial, duplicate, ambiguous, or failed responses activate nothing.

**Verification:** Fixture tests cover rename, changed description, duplicate name, reordered category, source-reference drift, partial response, and removal. Run one isolated live anonymous probe in a temporary data root and verify the second run uses stable cache equality.
