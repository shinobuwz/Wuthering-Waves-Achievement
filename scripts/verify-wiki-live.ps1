[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("wuwa-live-wiki-" + [Guid]::NewGuid().ToString('N'))
Push-Location $nativeRoot
try {
    $env:WUWA_RUN_LIVE_WIKI = '1'
    $env:WUWA_NATIVE_DATA_ROOT = $root
    & dotnet test WutheringWavesAchievement.sln -c Release --no-restore --filter "FullyQualifiedName~WikiLiveProbe"
    if ($LASTEXITCODE -ne 0) { throw "Live Wiki probe failed with exit code $LASTEXITCODE." }
}
finally {
    Remove-Item Env:WUWA_RUN_LIVE_WIKI -ErrorAction SilentlyContinue
    Remove-Item Env:WUWA_NATIVE_DATA_ROOT -ErrorAction SilentlyContinue
    Pop-Location
    if (Test-Path $root) { Remove-Item $root -Recurse -Force }
}
