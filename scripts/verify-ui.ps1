[CmdletBinding()]
param([string]$OutputDirectory = '', [string]$Executable = '')
$ErrorActionPreference = 'Stop'
$nativeRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $nativeRoot 'artifacts/ui' }
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $nativeRoot 'src/Wuwa.App/bin/Release/net8.0-windows/WutheringWavesAchievement.exe' }
if (-not (Test-Path $Executable)) { throw "Build the Release app before UI verification: $Executable" }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'UI verification must run from an elevated PowerShell because the app manifest requires administrator.' }
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
Get-ChildItem $OutputDirectory -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item -Force

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeUiVerification {
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);
}
'@
function Find-ByAutomationId($root, [string]$id) {
    $condition = New-Object Windows.Automation.PropertyCondition -ArgumentList ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-ByName($root, [string]$name) {
    $condition = New-Object Windows.Automation.PropertyCondition -ArgumentList ([Windows.Automation.AutomationElement]::NameProperty), $name
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-ForId($process, [string]$id, [int]$attempts = 40) {
    for ($attempt = 0; $attempt -lt $attempts -and -not $process.HasExited; $attempt++) {
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            $root = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$process.MainWindowHandle)
            $found = Find-ByAutomationId $root $id
            if ($found) { return $found }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Required UI control is not reachable: $id"
}
function Wait-ShellReady($process) {
    for ($attempt = 0; $attempt -lt 100 -and -not $process.HasExited; $attempt++) {
        $status = Wait-ForId $process 'ShellStatusText'
        if ($status.Current.Name -match '^显示 \d+ 条') { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'The modular shell did not finish workspace initialization.'
}
function Invoke-Id($process, [string]$id) {
    $element = Wait-ForId $process $id
    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 150
}
function Assert-Ids($process, [string[]]$ids) { foreach ($id in $ids) { $null = Wait-ForId $process $id } }
function Wait-ForEnabled($process, [string]$id, [bool]$enabled) {
    for ($attempt = 0; $attempt -lt 40 -and -not $process.HasExited; $attempt++) {
        $element = Wait-ForId $process $id
        if ($element.Current.IsEnabled -eq $enabled) { return $element }
        Start-Sleep -Milliseconds 100
    }
    if ($id -eq 'RotationStartButton') {
        $validation = Wait-ForId $process 'RotationValidationText'
        throw "Control '$id' did not reach IsEnabled=$enabled. Validation=$($validation.Current.Name)"
    }
    throw "Control '$id' did not reach IsEnabled=$enabled."
}
function Capture-Binding($process, [string]$id, [string]$keys) {
    $process.Refresh()
    [void][NativeUiVerification]::SetForegroundWindow([IntPtr]$process.MainWindowHandle)
    $element = Wait-ForId $process $id
    $element.SetFocus()
    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 100
    [void][NativeUiVerification]::SetForegroundWindow([IntPtr]$process.MainWindowHandle)
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 250
}

$dataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("wuwa-ui-" + [Guid]::NewGuid().ToString('N'))
New-Item (Join-Path $dataRoot 'rotations/profiles') -ItemType Directory -Force | Out-Null
$profileId = [Guid]::NewGuid()
@{
    schemaVersion=1; id=$profileId.ToString('D'); name='UI verification profile'; initialSlot=1
    team=@(@{slot=1; characterName='Verifier'; alias=$null})
    opener=@(); loop=@(@{action='Basic'; description='Basic verification'; variant=$null; targetSlot=$null; iconReference=$null})
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $dataRoot ("rotations/profiles/{0}.json" -f $profileId.ToString('N'))) -Encoding utf8
$hekiliFixture = Join-Path $dataRoot 'hekili-ui-fixture.json'
@{
    name='Imported UI fixture'; team_config=@{'1'='Verifier'}; team_aliases=@{'1'='UI'}; initial_char_index=1
    opener_script=@(@{type='skill';desc='Imported skill'}); loop_script=@()
} | ConvertTo-Json -Depth 6 | Set-Content $hekiliFixture -Encoding utf8
@{
    schemaVersion=1; heavyThresholdMilliseconds=500; selectedProfileId=$profileId.ToString('D')
    bindings=@(
        @{action='Start';device='Keyboard';code=116},
        @{action='Reset';device='Keyboard';code=117},
        @{action='Reselect';device='Keyboard';code=118},
        @{action='Basic';device='Mouse';code=1}
    )
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $dataRoot 'rotations/settings.json') -Encoding utf8
$env:WUWA_NATIVE_DATA_ROOT = $dataRoot
$env:WUWA_NATIVE_UI_ROTATION_IMPORT_FILE = $hekiliFixture
$process = $null
$captureProcess = $null
try {
    $process = Start-Process $Executable -PassThru
    for ($i = 0; $i -lt 100 -and $process.MainWindowHandle -eq 0 -and -not $process.HasExited; $i++) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'UI verification app did not expose a main window.' }
    Wait-ShellReady $process

    Assert-Ids $process @('DashboardPageRoot','DashboardNavigationButton','AchievementsNavigationButton','RotationNavigationButton','GameToolsNavigationButton','SettingsNavigationButton','WorkspaceHelpButton')

    Invoke-Id $process 'AchievementsNavigationButton'
    Assert-Ids $process @('AchievementsPageRoot','TrackSelectedButton','UntrackSelectedButton','OpenTrackerButton','TrackingHelpButton','OcrWorkbenchButton','OcrHelpButton','LegacyImportButton','ExchangeImportButton','ExchangeExportButton','WikiSyncButton','DataHelpButton','AchievementGrid')
    Invoke-Id $process 'OcrWorkbenchButton'
    Assert-Ids $process @('OcrWorkbenchPage','OcrBackButton')
    Invoke-Id $process 'OcrBackButton'
    $null = Wait-ForId $process 'AchievementsPageRoot'

    Invoke-Id $process 'RotationNavigationButton'
    Assert-Ids $process @('RotationPageRoot','RotationImportButton','RotationDeleteButton','RotationProfileList','RotationBindingsList','RotationValidationText','RotationStartButton','RotationBindingStartButton','RotationBindingBasicButton','RotationBindingSkillButton')
    $null = Wait-ForEnabled $process 'RotationStartButton' $true
    Capture-Binding $process 'RotationBindingBasicButton' '{F8}'
    $null = Wait-ForEnabled $process 'RotationStartButton' $true
    Capture-Binding $process 'RotationBindingStartButton' '{F8}'
    $null = Wait-ForEnabled $process 'RotationStartButton' $false
    Capture-Binding $process 'RotationBindingStartButton' '{F5}'
    $null = Wait-ForEnabled $process 'RotationStartButton' $true

    Invoke-Id $process 'RotationImportButton'
    $imported = $null
    for ($attempt = 0; $attempt -lt 50 -and -not $imported -and -not $process.HasExited; $attempt++) {
        $process.Refresh()
        $rootElement = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$process.MainWindowHandle)
        $imported = Find-ByName $rootElement 'Imported UI fixture'
        if (-not $imported) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $imported) { throw 'Imported Hekili fixture did not appear in the Rotation profile list.' }
    $null = Wait-ForEnabled $process 'RotationStartButton' $false
    Capture-Binding $process 'RotationBindingSkillButton' '{F9}'
    $null = Wait-ForEnabled $process 'RotationStartButton' $true

    Invoke-Id $process 'GameToolsNavigationButton'
    Assert-Ids $process @('GameToolsPageRoot','ConveneLinkButton','ConveneHelpButton','MapOverlayButton')

    Invoke-Id $process 'SettingsNavigationButton'
    Assert-Ids $process @('SettingsPageRoot','ThemeButton','UpdateButton','SystemHelpButton')

    Invoke-Id $process 'WorkspaceHelpButton'
    Assert-Ids $process @('WorkspaceHelpPage','WorkspaceHelpBackButton')
    Invoke-Id $process 'WorkspaceHelpBackButton'
    $null = Wait-ForId $process 'SettingsPageRoot'

    Invoke-Id $process 'DashboardNavigationButton'
    $null = Wait-ForId $process 'DashboardPageRoot'
    $process.Kill()
    $process.WaitForExit()

    $env:WUWA_NATIVE_UI_CAPTURE_DIR = $OutputDirectory
    $captureProcess = Start-Process $Executable -PassThru
    if (-not $captureProcess.WaitForExit(30000)) { throw 'UI verification capture app did not exit after producing screenshots.' }
    if ($captureProcess.ExitCode -ne 0) { throw "UI verification capture app exited with code $($captureProcess.ExitCode)." }
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
    if ($process -and -not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(5000) }
    if ($captureProcess -and -not $captureProcess.HasExited) { $captureProcess.Kill(); [void]$captureProcess.WaitForExit(5000) }
    Remove-Item Env:WUWA_NATIVE_DATA_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:WUWA_NATIVE_UI_ROTATION_IMPORT_FILE -ErrorAction SilentlyContinue
    Remove-Item Env:WUWA_NATIVE_UI_CAPTURE_DIR -ErrorAction SilentlyContinue
    if (Test-Path $dataRoot) { Remove-Item $dataRoot -Recurse -Force }
}
