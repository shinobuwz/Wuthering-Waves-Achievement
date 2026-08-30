[CmdletBinding()]
param(
    [string]$PackageDirectory = '',
    [string]$LegacyConfig = ''
)
$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Join-Path $nativeRoot 'publish/win-x64' }
$packageExe = Join-Path $PackageDirectory 'WutheringWavesAchievement.exe'
if (-not (Test-Path $packageExe)) { throw "Portable package was not found: $packageExe" }

$verificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wuwa-portable-lifecycle-" + [Guid]::NewGuid().ToString('N'))
$dataRoot = Join-Path $verificationRoot 'data'
$packageA = Join-Path $verificationRoot 'package-a'
$packageB = Join-Path $verificationRoot 'package-b'
$packageC = Join-Path $verificationRoot 'package-c'
New-Item $verificationRoot -ItemType Directory | Out-Null
Copy-Item $PackageDirectory $packageA -Recurse
Copy-Item $PackageDirectory $packageB -Recurse
Copy-Item $PackageDirectory $packageC -Recurse

$rotationProfileId = [Guid]::NewGuid()
$rotationProfileDirectory = Join-Path $dataRoot 'rotations/profiles'
$rotationSettingsPath = Join-Path $dataRoot 'rotations/settings.json'
$rotationProfilePath = Join-Path $rotationProfileDirectory ($rotationProfileId.ToString('N') + '.json')
New-Item $rotationProfileDirectory -ItemType Directory -Force | Out-Null
@{
    schemaVersion=1; id=$rotationProfileId.ToString('D'); name='Portable lifecycle rotation'; initialSlot=1
    team=@(@{slot=1; characterName='Verifier'; alias=$null})
    opener=@(); loop=@(@{action='Basic'; description='Portable verification'; variant=$null; targetSlot=$null; iconReference=$null})
} | ConvertTo-Json -Depth 8 | Set-Content $rotationProfilePath -Encoding utf8
@{
    schemaVersion=1; heavyThresholdMilliseconds=500; selectedProfileId=$rotationProfileId.ToString('D')
    bindings=@(
        @{action='Start';device='Keyboard';code=116},
        @{action='Reset';device='Keyboard';code=117},
        @{action='Reselect';device='Keyboard';code=118},
        @{action='Basic';device='Mouse';code=1}
    )
} | ConvertTo-Json -Depth 6 | Set-Content $rotationSettingsPath -Encoding utf8
$rotationHashes = @{
    $rotationProfilePath = (Get-FileHash $rotationProfilePath -Algorithm SHA256).Hash
    $rotationSettingsPath = (Get-FileHash $rotationSettingsPath -Algorithm SHA256).Hash
}

function Assert-RotationState {
    foreach ($path in $rotationHashes.Keys) {
        if (-not (Test-Path $path)) { throw "Portable lifecycle removed Rotation state: $path" }
        if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $rotationHashes[$path]) { throw "Portable lifecycle changed Rotation state: $path" }
    }
}

function Start-Bounded([string]$Executable, [string[]]$Arguments = @()) {
    $env:WUWA_NATIVE_DATA_ROOT = $dataRoot
    $process = if ($Arguments.Count -gt 0) { Start-Process $Executable -ArgumentList $Arguments -PassThru } else { Start-Process $Executable -PassThru }
    Start-Sleep -Seconds 5
    if ($process.HasExited -and $process.ExitCode -ne 0) { throw "Package launch failed with exit code $($process.ExitCode)." }
    if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; if (-not $process.WaitForExit(5000)) { $process.Kill() } }
}

try {
    $args = @()
    $legacyHashes = @{}
    if (-not [string]::IsNullOrWhiteSpace($LegacyConfig)) {
        $legacyConfigPath = (Resolve-Path $LegacyConfig).Path
        $legacyDirectory = Split-Path -Parent $legacyConfigPath
        Get-ChildItem $legacyDirectory -File | ForEach-Object { $legacyHashes[$_.FullName] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
        $args = @('--legacy-config', $legacyConfigPath, '--auto-import-legacy')
    }
    Start-Bounded (Join-Path $packageA 'WutheringWavesAchievement.exe') $args
    Assert-RotationState
    if (-not (Test-Path (Join-Path $dataRoot 'current.json'))) { throw 'First launch did not create native state.' }
    $firstManifestHash = (Get-FileHash (Join-Path $dataRoot 'current.json') -Algorithm SHA256).Hash
    $firstManifest = Get-Content (Join-Path $dataRoot 'current.json') -Raw | ConvertFrom-Json
    $firstStatePath = Join-Path (Join-Path (Join-Path $dataRoot 'generations') $firstManifest.generation) 'state.json'
    $firstState = Get-Content $firstStatePath -Raw | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace($LegacyConfig)) {
        if ([string]::IsNullOrWhiteSpace($firstState.metadata.profileUid)) { throw 'Legacy migration profile UID was not activated.' }
        $expectedProgress = (Resolve-Path (Join-Path $legacyDirectory ("user_progress_" + $firstState.metadata.profileUid + ".json"))).Path
        if ($firstState.metadata.legacySourcePath -ne $expectedProgress) { throw 'Legacy migration source path was not activated from the selected fixture.' }
        $completedStatus = ([char]0x5df2).ToString() + [char]0x5b8c + [char]0x6210
        if (-not ($firstState.statuses.PSObject.Properties.Value -contains $completedStatus)) { throw 'Legacy migration did not preserve any completed status.' }
    }

    Start-Bounded (Join-Path $packageB 'WutheringWavesAchievement.exe')
    Assert-RotationState
    $upgradeManifest = Get-Content (Join-Path $dataRoot 'current.json') -Raw | ConvertFrom-Json
    if ($upgradeManifest.generation -ne $firstManifest.generation) { throw 'Upgrade launch unexpectedly replaced the active native generation.' }
    Remove-Item $packageA, $packageB -Recurse -Force
    if (-not (Test-Path (Join-Path $dataRoot 'current.json'))) { throw 'Portable uninstall simulation removed user state.' }
    Assert-RotationState

    Start-Bounded (Join-Path $packageC 'WutheringWavesAchievement.exe')
    Assert-RotationState
    $reinstallManifest = Get-Content (Join-Path $dataRoot 'current.json') -Raw | ConvertFrom-Json
    if ($reinstallManifest.generation -ne $firstManifest.generation) { throw 'Reinstall launch unexpectedly replaced the active native generation.' }
    foreach ($path in $legacyHashes.Keys) {
        if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $legacyHashes[$path]) { throw "Legacy fixture was modified: $path" }
    }
    Write-Host "Portable lifecycle verification passed. Initial manifest SHA256: $firstManifestHash"
}
finally {
    Remove-Item Env:WUWA_NATIVE_DATA_ROOT -ErrorAction SilentlyContinue
    if (Test-Path $verificationRoot) { Remove-Item $verificationRoot -Recurse -Force }
}
