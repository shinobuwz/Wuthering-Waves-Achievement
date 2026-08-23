[CmdletBinding()]
param([string]$OutputDirectory = '', [string]$Executable = '')
$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $nativeRoot 'artifacts/ui' }
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $nativeRoot 'src/Wuwa.App/bin/Release/net8.0-windows/WutheringWavesAchievement.exe' }
if (-not (Test-Path $Executable)) { throw "Build the Release app before UI verification: $Executable" }
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
Get-ChildItem $OutputDirectory -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
function Find-ByAutomationId($root, [string]$id) {
    $condition = New-Object Windows.Automation.PropertyCondition -ArgumentList ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

$dataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wuwa-ui-" + [Guid]::NewGuid().ToString('N'))
$env:WUWA_NATIVE_DATA_ROOT = $dataRoot
$env:WUWA_NATIVE_UI_CAPTURE_DIR = $OutputDirectory
$process = $null
try {
    $process = Start-Process $Executable -PassThru
    for ($i = 0; $i -lt 100 -and $process.MainWindowHandle -eq 0 -and -not $process.HasExited; $i++) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    if (-not $process.HasExited -and $process.MainWindowHandle -ne 0) {
        $required = @('ThemeButton','TrackSelectedButton','UntrackSelectedButton','OpenTrackerButton','TrackingHelpButton','MapOverlayButton','OcrSearchSyncButton','OcrSearchInputTestButton','OcrHelpButton','LegacyImportButton','ExchangeImportButton','ExchangeExportButton','WikiSyncButton','DataHelpButton','UpdateButton','SystemHelpButton','AchievementGrid')
        foreach ($id in $required) {
            $found = $null
            for ($attempt = 0; $attempt -lt 30 -and -not $found -and -not $process.HasExited; $attempt++) {
                $process.Refresh()
                $root = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$process.MainWindowHandle)
                $found = Find-ByAutomationId $root $id
                if (-not $found) { Start-Sleep -Milliseconds 100 }
            }
            if (-not $found -and -not $process.HasExited) { throw "Required UI control is not reachable: $id" }
        }
    }
    if (-not $process.WaitForExit(30000)) { throw 'UI verification app did not exit after producing screenshots.' }
    if ($process.ExitCode -ne 0) { throw "UI verification app exited with code $($process.ExitCode)." }
    $expected = @('1080x700-dark.png','1080x700-light.png','1440x900-dark.png','1440x900-light.png')
    foreach ($name in $expected) {
        $path = Join-Path $OutputDirectory $name
        if (-not (Test-Path $path) -or (Get-Item $path).Length -lt 10000) { throw "Screenshot is missing or blank: $path" }
    }
    foreach ($file in Get-ChildItem $OutputDirectory -Filter '*.png' | Sort-Object Name) {
        Write-Host "$($file.Name) $($file.Length) bytes SHA256=$((Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
    }
}
finally {
    if ($process -and -not $process.HasExited) { $process.Kill() }
    Remove-Item Env:WUWA_NATIVE_DATA_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:WUWA_NATIVE_UI_CAPTURE_DIR -ErrorAction SilentlyContinue
    if (Test-Path $dataRoot) { Remove-Item $dataRoot -Recurse -Force }
}
