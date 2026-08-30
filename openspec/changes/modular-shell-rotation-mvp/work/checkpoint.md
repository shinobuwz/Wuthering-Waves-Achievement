# Working Checkpoint

## Change

- ID：`modular-shell-rotation-mvp`
- Branch：`opsx/modular-shell-rotation-mvp`
- Owner：Main/controller

## Snapshot

- Base/HEAD before Commit A：`1240c7384c3b842b5827c144cca814f320eb8245`
- Scope fingerprint：modular Native WPF shell + read-only keyboard/mouse Rotation MVP; no controller/editor/icon capture/legacy asset/new gacha export.
- Current state：all implementation and verification complete; working tree contains only current-change paths; Commit A/B not yet created.

## Frontier

- Tasks 01–05：complete.
- Current frontier：path-limited Commit A → bounded knowledge finalization → current change cleanup and Commit B.
- Blocked by：none.

## Completed

- Task 01：Dashboard-first modular shell, sidebar modules, extracted Achievement workspace, relocated Game Tools and retained settings/help/theme/update behavior.
- Task 02：versioned profiles/settings, strict atomic Hekili import, binding validation and public `RotationRunSession` semantics.
- Task 03：profile list/select/delete/import, keyboard/mouse capture, explicit validation and start gating.
- Task 04：foreground-bound Windows monitor, injected-event-filtering read-only hooks, three-slot overlay, fixed stop shortcut and cleanup coordinator; direct real-game keyboard/mouse pass-through passed.
- Task 05：README/help copy, immutable badge packaging, UI/tracker/rotation verification scripts, Rotation-aware portable lifecycle checks and required real-game smoke.

## Open Findings

- F-001 MainWindow concentration：closed by extracted Achievement visual namescope and modular views while preserving command boundaries.
- F-002 input safety boundary：closed by architecture, static scan, public tests, elevated surrogate and real-game behavior.
- F-004 real-game physical-input acceptance：closed; all checklist items passed.
- F-003 data-root knowledge mismatch：accepted handoff item. Post-Commit-A knowledge finalization must amend stale `%LocalAppData%` wording to the source-authoritative `<program>\data` plus `WUWA_NATIVE_DATA_ROOT` override and capture modular-shell/Rotation boundaries where directly associated.

## Latest Verification

- Snapshot：current working tree immediately before Commit A; no product-code change after verification.
- Focused Rotation tests：23 passed, 0 failed, 0 skipped.
- Fresh full Release tests：105 passed, 0 failed, 4 environment-gated skipped, 109 total.
- Fresh Release build：0 warnings, 0 errors.
- Native publish：OCR 2/2 passed; clean 27-file directory / 177,748,342 bytes; 26 manifest entries with zero mismatch; ZIP SHA256 `b66c264312f269d562f2a9d77fb18d5b166b9fb72dbcedaacb7019f3df68a959`.
- Elevated final suite：UI PASS, tracker PASS, visible Rotation surrogate PASS, portable lifecycle PASS (`artifacts/elevated-final/summary.json`).
- Real《鸣潮》smoke：PASS. High-integrity app/game; 1920×1080 client; focus safety; no automatic action; wrong Space passed through without progression; F5/mouse-left/E passed through and advanced exact steps; Alt-Tab pause/resume; fixed stop and restore (`artifacts/real-game-smoke/final-summary.md`).
- Static/repository：Rotation safety PASS; PowerShell parsers PASS; `git diff --check` content-clean; all task checkboxes complete; diff ownership complete.
- Residual release risk：none.

## Next Packet

1. Path-limit stage all listed current-change paths and create Commit A.
2. Build a version-1 knowledge finalization packet with `change_path`, Commit A as the unique stable source ref, and run `shino knowledge --project <root> finalize-plan <packet.json>`.
3. Complete every required keep/amend/stale/merge/delete disposition with one knowledge writer; run checker and required capture verification.
4. Only on `captured|zero-write` with `cleanup_eligible: true`, delete the entire current change root and create Commit B containing knowledge/capture disposition plus cleanup.
5. Inspect Commit A/B and prove a clean worktree before `goal_complete`.

## Do Not Repeat

- Product/scope decisions are final.
- Managed, elevated surrogate, package/portable and real-game acceptance are complete and green.
- Do not alter product behavior unless knowledge finalization exposes a direct source contradiction.
- Do not create archive or Commit C; Commit B must remove the current change root.
