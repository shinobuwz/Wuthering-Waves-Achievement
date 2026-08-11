# 05 — Refined desktop shell and secure updates

**What to build:** Complete the WPF management, statistics, and data views using a restrained charcoal/teal game-tool design, dark/light resources, virtualized table interaction, loading/empty/error states, dialogs, keyboard navigation, and secure GitHub release checking.

**Blocked by:** 01, 02

**Suggested Files:** `native/src/Wuwa.App/`, `native/src/Wuwa.Infrastructure/Updates/`, `native/tests/Wuwa.Tests/Ui/`

**Behavior Context:**

- The first screen is the usable workspace, not a landing page.
- Keep page information dense and operational; do not restore avatar, promotion, help, authentication, or multi-user surfaces.
- Explicit commands replace the legacy one-second long-press-only status behavior while preserving all four statuses.
- Minimum viewport is 1080x700; 1440x900 is the primary review viewport.
- Update checks use normal TLS validation and never open a browser automatically.

**Acceptance:** All scoped workflows are visually complete and keyboard-usable in both themes; state changes are observable through `AchievementWorkspace`; no overlap, clipping, blank charts, or inaccessible command states occur at target viewports.

**Verification:** UI Automation drives launch/load/filter/status/theme/error scenarios. Capture and inspect dark/light screenshots at 1080x700 and 1440x900. Use fixture HTTP handlers for latest/update/error cases.
