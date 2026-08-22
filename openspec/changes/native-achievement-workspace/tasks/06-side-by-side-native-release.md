# 06 — Side-by-side native release

**What to build:** Pin the supported SDK/toolchain, produce a self-contained `win-x64` package, run packaged migration and user-data lifecycle checks, and document how the native application coexists with the legacy Python version until later changes complete parity.

**Blocked by:** 03, 04, 05

**Suggested Files:** `global.json`, `Directory.Build.props`, `scripts/`, `README.md`, `docx/项目架构分析.md`

**Behavior Context:**

- Do not delete or modify Python/Qt sources, models, entry points, requirements, or legacy data in this change.
- Published immutable assets may live with the application; mutable generations live under LocalAppData.
- Install, upgrade, and uninstall operations must not remove native user state.
- The package must identify missing/corrupt immutable resources with a visible error instead of creating an empty replacement library.

**Acceptance:** A clean Windows x64 package launches without Python, loads shipped resources, imports copied legacy fixtures, retains state across relaunch/upgrade/uninstall simulation, and clearly documents current non-OCR scope.

**Verification:** Run full tests, Release build and self-contained publish, start the published executable against a temporary LocalAppData root, exercise migration and relaunch, and inspect the final scoped diff and package contents.
