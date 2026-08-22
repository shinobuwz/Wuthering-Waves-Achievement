[CmdletBinding()]
param([string]$Executable = '')
$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $nativeRoot 'src/Wuwa.App/bin/Release/net8.0-windows/Wuwa.App.exe' }
if (-not (Test-Path $Executable)) { throw "Build the Release app before tracker UI verification: $Executable" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
function Find-ByAutomationId($root, [string]$id) {
    $condition = New-Object Windows.Automation.PropertyCondition -ArgumentList ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

$dataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wuwa-tracker-ui-" + [Guid]::NewGuid().ToString('N'))
$env:WUWA_NATIVE_DATA_ROOT = $dataRoot
$process = $null
try {
    $process = Start-Process $Executable -PassThru
    for ($i = 0; $i -lt 100 -and $process.MainWindowHandle -eq 0 -and -not $process.HasExited; $i++) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'Tracker UI verification app did not expose a main window.' }

    $main = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$process.MainWindowHandle)
    $open = $null
    for ($attempt = 0; $attempt -lt 30 -and -not $open; $attempt++) {
        $open = Find-ByAutomationId $main 'OpenTrackerButton'
        if (-not $open) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $open) { throw 'OpenTrackerButton is not reachable.' }
    $invoke = $open.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()

    $desktop = [Windows.Automation.AutomationElement]::RootElement
    $tracker = $null
    for ($attempt = 0; $attempt -lt 30 -and -not $tracker; $attempt++) {
        $tracker = Find-ByAutomationId $desktop 'TrackerWindow'
        if (-not $tracker) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $tracker) { throw 'TrackerWindow is not reachable after opening the tracker.' }
    foreach ($id in @('TrackerSearchBox', 'TrackerClearButton', 'TrackerExpandWorkspaceButton', 'TrackerCloseButton', 'TrackerReturnToGameButton', 'TrackerScrollViewer')) {
        if (-not (Find-ByAutomationId $tracker $id)) { throw "Tracker control is not reachable: $id" }
    }
    Write-Host 'TrackerWindow and required controls are reachable.'
}
finally {
    if ($process -and -not $process.HasExited) { $process.Kill() }
    Remove-Item Env:WUWA_NATIVE_DATA_ROOT -ErrorAction SilentlyContinue
    if (Test-Path $dataRoot) { Remove-Item $dataRoot -Recurse -Force }
}
