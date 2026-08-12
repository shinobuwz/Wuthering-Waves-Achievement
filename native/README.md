# Native Wuthering Waves Achievement Workspace

This directory contains the side-by-side Windows-native workspace rewrite.

## Build

```powershell
dotnet restore native/WutheringWavesAchievement.sln
dotnet test native/WutheringWavesAchievement.sln
dotnet build native/WutheringWavesAchievement.sln -c Release
dotnet publish native/src/Wuwa.App/Wuwa.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o native/publish/win-x64
```

The application targets .NET 8 / `net8.0-windows` and does not require Python at runtime. Shipped immutable resources are copied beside the executable. Mutable native generations are stored under `%LocalAppData%\WutheringWavesAchievement`.

For bounded smoke tests, set `WUWA_NATIVE_DATA_ROOT` to a temporary directory. Legacy files are read-only inputs; native status changes never write `resources/config.json` or `resources/user_progress_*.json`.

The native application currently covers management, four progress statuses, grouped transitions, filtering/statistics, transactional generations, explicit legacy import, anonymous Wiki reconciliation seam, JSON and TSV/Excel-compatible exchange, theme switching, and GitHub release checking. Native OCR, game automation, and overlay behavior are intentionally outside this workspace release.
