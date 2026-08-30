# Implementation review receipt

## Scope reviewed

- `SceneMarkerFrameTools` and identifier contracts.
- `SceneMarkerStorage` persistence and metadata.
- WPF marker overlay XAML/code-behind.
- Main-window capture orchestration and feature visibility.
- Focused marker tests and OpenSpec contract.

## Review method

Parallel review perspectives:

1. correctness and edge cases;
2. architecture and repository boundaries;
3. tests and interaction behavior;
4. synthesized severity/deduplication pass.

## Findings resolved

- Suspended/restored the Kuro map overlay and blocked map toggles during marker capture.
- Removed pre-HWND physical coordinates assigned as WPF DIPs and failed closed on native positioning errors.
- Validated full pre/post game client bounds rather than dimensions only.
- Prevented close/Alt+F4 while persistence is active.
- Derived marker BGR from authoritative source+ROI, validated PNG chunks/CRCs/dimensions, and split BGR/PNG hashes.
- Probed the actual scene destination so fallback is offered for scene-path failures.
- Added stable automation IDs/names and live status/error announcements.
- Updated focused tests and verification artifacts.

## Disposition

No unresolved release-blocking finding remains for the agreed capture-only scope. Mixed-DPI multi-monitor behavior and forced permission-denied picker smoke are recorded as follow-up verification notes rather than silently claimed as covered.
