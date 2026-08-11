# 02 — Transactional state and legacy import

**What to build:** Implement `JsonAppDataStore` as validated versioned generations with an atomic current-manifest pointer. Add explicit discovery and import for one legacy profile, deterministic profile selection behavior, rollback history, and structured migration errors.

**Blocked by:** 01

**Suggested Files:** `native/src/Wuwa.Infrastructure/Persistence/`, `native/src/Wuwa.Core/Migration/`, `native/tests/Wuwa.Tests/Persistence/`, `native/tests/Wuwa.Tests/Fixtures/Legacy/`

**Behavior Context:**

- Never modify files under the legacy `resources/` directory.
- A candidate generation is not current until every required document and reference validates.
- An invalid `current_user`, duplicate nickname, missing UID progress file, corrupt JSON, or multiple candidate profiles must not silently choose the wrong progress.
- Re-import is an explicit native-state replacement with prior generation retained.

**Acceptance:** Valid selected progress imports with all statuses preserved; interrupted writes leave a valid generation active; invalid migration leaves no success marker and no partial active state.

**Verification:** Run the workspace contract against both adapters, migration fixtures for all profile-selection branches, and fault injection at write/flush/validate/manifest-replace boundaries.
