[CmdletBinding()]
param([string]$Executable = '')
$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $nativeRoot 'src/Wuwa.App/bin/Release/net8.0-windows/WutheringWavesAchievement.exe' }
if (-not (Test-Path $Executable)) { throw "Build the Release app before tracker UI verification: $Executable" }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Tracker UI verification must run from an elevated PowerShell because the app manifest requires administrator.' }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
function Find-ByAutomationId($root, [string]$id) {
    $condition = New-Object Windows.Automation.PropertyCondition -ArgumentList ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-MainControl($process, [string]$id) {
    for ($attempt = 0; $attempt -lt 50 -and -not $process.HasExited; $attempt++) {
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            $main = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$process.MainWindowHandle)
            $found = Find-ByAutomationId $main $id
            if ($found) { return $found }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Main-window control is not reachable: $id"
}
function Wait-ShellReady($process) {
    for ($attempt = 0; $attempt -lt 100 -and -not $process.HasExited; $attempt++) {
        $status = Wait-MainControl $process 'ShellStatusText'
        if ($status.Current.Name -match '^显示 \d+ 条') { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'The modular shell did not finish workspace initialization.'
}

$dataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wuwa-tracker-ui-" + [Guid]::NewGuid().ToString('N'))
$env:WUWA_NATIVE_DATA_ROOT = $dataRoot
$process = $null
try {
    $process = Start-Process $Executable -PassThru
    for ($i = 0; $i -lt 100 -and $process.MainWindowHandle -eq 0 -and -not $process.HasExited; $i++) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'Tracker UI verification app did not expose a main window.' }
    Wait-ShellReady $process
    $null = Wait-MainControl $process 'DashboardPageRoot'

    $nav = Wait-MainControl $process 'AchievementsNavigationButton'
    $nav.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
    $open = Wait-MainControl $process 'OpenTrackerButton'
    $open.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()

    $desktop = [Windows.Automation.AutomationElement]::RootElement
    $tracker = $null
    for ($attempt = 0; $attempt -lt 40 -and -not $tracker; $attempt++) {
        $tracker = Find-ByAutomationId $desktop 'TrackerWindow'
        if (-not $tracker) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $tracker) { throw 'TrackerWindow is not reachable after opening the tracker.' }
    foreach ($id in @('TrackerSearchBox', 'TrackerClearButton', 'TrackerExpandWorkspaceButton', 'TrackerCloseButton', 'TrackerReturnToGameButton', 'TrackerScrollViewer')) {
        if (-not (Find-ByAutomationId $tracker $id)) { throw "Tracker control is not reachable: $id" }
    }

    $expand = Find-ByAutomationId $tracker 'TrackerExpandWorkspaceButton'
    $expand.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $null = Wait-MainControl $process 'AchievementsPageRoot'
    $null = Wait-MainControl $process 'OpenTrackerButton'
    Write-Host 'Tracker controls are reachable and restore returns to the Achievements route.'
}
finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(5000) }
    Remove-Item Env:WUWA_NATIVE_DATA_ROOT -ErrorAction SilentlyContinue
    if (Test-Path $dataRoot) { Remove-Item $dataRoot -Recurse -Force }
}
