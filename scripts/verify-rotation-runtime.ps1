[CmdletBinding()]
param([string]$Executable = '', [switch]$RunWindowSmoke)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $root 'src/Wuwa.App/bin/Release/net8.0-windows/WutheringWavesAchievement.exe' }

$rotationFiles = Get-ChildItem (Join-Path $root 'src') -Recurse -File -Filter '*Rotation*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
$forbidden = @('SendInput','mouse_event','keybd_event','PostMessage','ReadProcessMemory','WriteProcessMemory','OpenProcess','CreateRemoteThread','WindowsGameWindowCapture','IGameWindowCapture')
$scopes = @{}
foreach ($file in $rotationFiles) { $scopes[$file.FullName] = Get-Content $file.FullName -Raw }
$mainWindowText = Get-Content (Join-Path $root 'src/Wuwa.App/MainWindow.xaml.cs') -Raw
$rotationEntryStart = $mainWindowText.IndexOf('private async Task StartRotationAsync', [StringComparison]::Ordinal)
$rotationEntryEnd = $mainWindowText.IndexOf('private void RestoreFromRotation', $rotationEntryStart, [StringComparison]::Ordinal)
$mapHandoffStart = $mainWindowText.IndexOf('private void HideMapOverlayForRotation', [StringComparison]::Ordinal)
$mapHandoffEnd = $mainWindowText.IndexOf('private void ShowMapError', $mapHandoffStart, [StringComparison]::Ordinal)
if ($rotationEntryStart -lt 0 -or $rotationEntryEnd -le $rotationEntryStart -or $mapHandoffStart -lt 0 -or $mapHandoffEnd -le $mapHandoffStart) { throw 'Unable to locate the MainWindow Rotation entry scopes for safety review.' }
$scopes['MainWindow.StartRotationAsync'] = $mainWindowText.Substring($rotationEntryStart, $rotationEntryEnd - $rotationEntryStart)
$scopes['MainWindow.HideMapOverlayForRotation'] = $mainWindowText.Substring($mapHandoffStart, $mapHandoffEnd - $mapHandoffStart)
foreach ($scope in $scopes.GetEnumerator()) {
    foreach ($token in $forbidden) {
        if ($scope.Value.Contains($token)) { throw "Forbidden Rotation dependency '$token' in $($scope.Key)." }
    }
}
$inputSource = Get-Content (Join-Path $root 'src/Wuwa.Infrastructure/WindowsRotationRuntime.cs') -Raw
foreach ($token in @('CallNextHookEx','KeyboardInjected','KeyboardLowerIntegrityInjected','MouseInjected')) {
    if (-not $inputSource.Contains($token)) { throw "Rotation input observer is missing required pass-through/filter marker: $token" }
}
Write-Host "Rotation static safety boundary passed for $($rotationFiles.Count) dedicated production files plus the MainWindow start/map-handoff entry scopes."

if (-not $RunWindowSmoke -and $env:WUWA_ROTATION_WINDOW_SMOKE -ne '1') {
    Write-Host 'Rotation window smoke skipped. Re-run with -RunWindowSmoke (same integrity level as the app) for the visible test-window lifecycle.'
    exit 0
}
if (-not (Test-Path $Executable)) { throw "Build the Release app before rotation window smoke: $Executable" }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Rotation window smoke must run from an elevated PowerShell because the app manifest requires administrator.' }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('wuwa-rotation-smoke-' + [Guid]::NewGuid().ToString('N'))
$dataRoot = Join-Path $temp 'data'
$gameExe = Join-Path $temp 'Client-Win64-Shipping.exe'
New-Item $temp -ItemType Directory -Force | Out-Null
New-Item (Join-Path $dataRoot 'rotations/profiles') -ItemType Directory -Force | Out-Null
$smokeSource = @'
using System;
using System.Drawing;
using System.Windows.Forms;
public static class RotationSmokeGame {
    [STAThread]
    public static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new Form {
            Text = "Rotation Smoke Game",
            StartPosition = FormStartPosition.Manual,
            Left = 100,
            Top = 100,
            Width = 1000,
            Height = 700,
            BackColor = Color.FromArgb(24, 33, 36)
        };
        var label = new Label {
            Dock = DockStyle.Fill,
            Text = "Visible read-only Rotation smoke target",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold)
        };
        form.Controls.Add(label);
        Application.Run(form);
    }
}
'@
$smokeSourcePath = Join-Path $temp 'RotationSmokeGame.cs'
Set-Content $smokeSourcePath $smokeSource -Encoding utf8
$csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
& $csc /nologo /target:winexe /out:$gameExe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $smokeSourcePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $gameExe)) { throw 'Unable to compile the visible Rotation smoke target.' }
$profileId = [Guid]::NewGuid()
$profilePath = Join-Path $dataRoot ("rotations/profiles/{0}.json" -f $profileId.ToString('N'))
@{
    schemaVersion = 1; id = $profileId.ToString('D'); name = 'Window smoke'; initialSlot = 1
    team = @(@{ slot = 1; characterName = 'Smoke'; alias = $null })
    opener = @(@{ action = 'Basic'; description = 'Physical left click'; variant = $null; targetSlot = $null; iconReference = $null })
    loop = @()
} | ConvertTo-Json -Depth 8 | Set-Content $profilePath -Encoding utf8
$bindings = @(
    @{ action='Start'; device='Keyboard'; code=116 }, @{ action='Reset'; device='Keyboard'; code=117 }, @{ action='Reselect'; device='Keyboard'; code=118 },
    @{ action='Basic'; device='Mouse'; code=1 }
)
@{ schemaVersion=1; bindings=$bindings; heavyThresholdMilliseconds=500; selectedProfileId=$profileId.ToString('D') } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $dataRoot 'rotations/settings.json') -Encoding utf8

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
function Find-Id($rootElement, [string]$id) {
    $condition = New-Object Windows.Automation.PropertyCondition -ArgumentList ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id
    $rootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-AppId($process, [string]$id, [int]$attempts = 80) {
    for ($attempt = 0; $attempt -lt $attempts -and -not $process.HasExited; $attempt++) {
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            $rootElement = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$process.MainWindowHandle)
            $found = Find-Id $rootElement $id
            if ($found) { return $found }
        }
        Start-Sleep -Milliseconds 100
    }
    return $null
}
function Wait-ShellReady($process) {
    for ($attempt = 0; $attempt -lt 100 -and -not $process.HasExited; $attempt++) {
        $status = Wait-AppId $process 'ShellStatusText'
        if ($status -and $status.Current.Name -match '^显示 \d+ 条') { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'The modular shell did not finish workspace initialization.'
}
$game = $null
$app = $null
$foreground = $null
$env:WUWA_NATIVE_DATA_ROOT = $dataRoot
try {
    $game = Start-Process $gameExe -PassThru
    for ($i=0; $i -lt 50 -and $game.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 100; $game.Refresh() }
    if ($game.MainWindowHandle -eq 0) { throw 'Visible smoke game window did not start.' }
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SmokeWindowNative {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hgt, uint f);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT rect);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT point);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
    [SmokeWindowNative]::SetWindowPos([IntPtr]$game.MainWindowHandle, [IntPtr]::Zero, 100, 100, 1000, 700, 0x0040) | Out-Null
    $app = Start-Process $Executable -PassThru
    for ($i=0; $i -lt 100 -and $app.MainWindowHandle -eq 0 -and -not $app.HasExited; $i++) { Start-Sleep -Milliseconds 100; $app.Refresh() }
    if ($app.MainWindowHandle -eq 0) { throw 'App main window did not start; run the smoke at the same integrity level as the app.' }
    Wait-ShellReady $app
    $null = Wait-AppId $app 'DashboardPageRoot'
    $nav = Wait-AppId $app 'RotationNavigationButton'; if (-not $nav) { throw 'Rotation navigation not found.' }
    $nav.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 300
    $start = Wait-AppId $app 'RotationStartButton'
    if (-not $start -or -not $start.Current.IsEnabled) {
        $validation = Wait-AppId $app 'RotationValidationText'
        $detail = if ($validation) { $validation.Current.Name } else { 'validation text unavailable' }
        throw "Seeded smoke profile is not runnable: $detail"
    }
    $start.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    $overlay = $null
    for ($i=0; $i -lt 50 -and -not $overlay; $i++) { $overlay = Find-Id ([Windows.Automation.AutomationElement]::RootElement) 'RotationOverlayWindow'; if (-not $overlay) { Start-Sleep -Milliseconds 100 } }
    if (-not $overlay) { throw 'Rotation overlay was not created.' }
    if ($overlay.Current.IsKeyboardFocusable) { throw 'Rotation overlay unexpectedly reports keyboard focusability.' }
    $overlayHandle = [IntPtr]$overlay.Current.NativeWindowHandle
    $styles = [SmokeWindowNative]::GetWindowLongPtr($overlayHandle, -20).ToInt64()
    if (($styles -band 0x08000000) -eq 0 -or ($styles -band 0x20) -eq 0) { throw 'Rotation overlay is missing WS_EX_NOACTIVATE or WS_EX_TRANSPARENT.' }
    if ([SmokeWindowNative]::SendMessage($overlayHandle, 0x0084, [IntPtr]::Zero, [IntPtr]::Zero).ToInt64() -ne -1) { throw 'Rotation overlay WM_NCHITTEST is not HTTRANSPARENT.' }
    if ([SmokeWindowNative]::SendMessage($overlayHandle, 0x0021, [IntPtr]::Zero, [IntPtr]::Zero).ToInt64() -ne 3) { throw 'Rotation overlay WM_MOUSEACTIVATE is not MA_NOACTIVATE.' }
    if ([SmokeWindowNative]::GetForegroundWindow() -ne [IntPtr]$game.MainWindowHandle) { throw 'Game window did not retain foreground after overlay creation.' }

    $client = New-Object SmokeWindowNative+RECT
    $origin = New-Object SmokeWindowNative+POINT
    if (-not [SmokeWindowNative]::GetClientRect([IntPtr]$game.MainWindowHandle, [ref]$client) -or -not [SmokeWindowNative]::ClientToScreen([IntPtr]$game.MainWindowHandle, [ref]$origin)) { throw 'Unable to read smoke game client bounds.' }
    $bounds = $overlay.Current.BoundingRectangle
    if ($bounds.Left -lt $origin.X -or $bounds.Top -lt $origin.Y -or $bounds.Right -gt ($origin.X + $client.Right) -or $bounds.Bottom -gt ($origin.Y + $client.Bottom)) { throw 'Rotation overlay is outside the game client bounds.' }
    $initialLeft = $bounds.Left; $initialTop = $bounds.Top; $initialPreview = $overlay.Current.Name

    [SmokeWindowNative]::SetWindowPos([IntPtr]$game.MainWindowHandle, [IntPtr]::Zero, 220, 160, 900, 650, 0x0040) | Out-Null
    [SmokeWindowNative]::SetForegroundWindow([IntPtr]$game.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 600
    $overlay = Find-Id ([Windows.Automation.AutomationElement]::RootElement) 'RotationOverlayWindow'
    if (-not $overlay) { throw 'Rotation overlay disappeared while following the game window.' }
    $moved = $overlay.Current.BoundingRectangle
    if ([Math]::Abs($moved.Left - $initialLeft) -lt 20 -and [Math]::Abs($moved.Top - $initialTop) -lt 20) { throw 'Rotation overlay did not follow the moved game client.' }
    $movedClient = New-Object SmokeWindowNative+RECT
    $movedOrigin = New-Object SmokeWindowNative+POINT
    if (-not [SmokeWindowNative]::GetClientRect([IntPtr]$game.MainWindowHandle, [ref]$movedClient) -or -not [SmokeWindowNative]::ClientToScreen([IntPtr]$game.MainWindowHandle, [ref]$movedOrigin)) { throw 'Unable to read moved smoke game client bounds.' }
    if ($moved.Left -lt $movedOrigin.X -or $moved.Top -lt $movedOrigin.Y -or $moved.Right -gt ($movedOrigin.X + $movedClient.Right) -or $moved.Bottom -gt ($movedOrigin.Y + $movedClient.Bottom)) { throw 'Rotation overlay left the moved game client bounds.' }

    $foregroundExe = Join-Path $temp 'Foreground-Smoke.exe'
    Copy-Item $gameExe $foregroundExe
    $foreground = Start-Process $foregroundExe -PassThru
    for ($i=0; $i -lt 50 -and $foreground.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 100; $foreground.Refresh() }
    if ($foreground.MainWindowHandle -eq 0) { throw 'Foreground-loss smoke window did not start.' }
    [SmokeWindowNative]::SetForegroundWindow([IntPtr]$foreground.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 500
    $pausedOverlay = Find-Id ([Windows.Automation.AutomationElement]::RootElement) 'RotationOverlayWindow'
    if ($pausedOverlay -and -not $pausedOverlay.Current.IsOffscreen) { throw 'Rotation overlay did not hide after foreground loss.' }
    [SmokeWindowNative]::SetForegroundWindow([IntPtr]$game.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 600
    $resumedOverlay = Find-Id ([Windows.Automation.AutomationElement]::RootElement) 'RotationOverlayWindow'
    if (-not $resumedOverlay -or $resumedOverlay.Current.IsOffscreen) { throw 'Rotation overlay did not resume when the game returned to foreground.' }
    if ($resumedOverlay.Current.Name -ne $initialPreview) { throw 'Rotation preview changed across foreground pause/resume without accepted input.' }
    if ($foreground -and -not $foreground.HasExited) { $foreground.Kill(); $foreground.WaitForExit() }

    $game.Kill(); $game.WaitForExit()
    $rotationRoot = $null
    for ($i=0; $i -lt 50 -and -not $rotationRoot; $i++) {
        $app.Refresh()
        if ($app.MainWindowHandle -ne 0) {
            $restoredMain = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$app.MainWindowHandle)
            $rotationRoot = Find-Id $restoredMain 'RotationPageRoot'
        }
        if (-not $rotationRoot) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $rotationRoot) { throw 'Main shell was not restored to the Rotation page after the test game closed.' }
    $remainingOverlay = Find-Id ([Windows.Automation.AutomationElement]::RootElement) 'RotationOverlayWindow'
    if ($remainingOverlay -and -not $remainingOverlay.Current.IsOffscreen) { throw 'Rotation overlay remained visible after game-loss cleanup.' }
    Write-Host 'Rotation visible test-window lifecycle passed: native NoActivate/click-through styles, client bounds/follow, foreground pause/resume, preview preservation and game-loss cleanup.'
}
finally {
    foreach ($process in @($app, $game, $foreground)) {
        if ($process -and -not $process.HasExited) {
            try { $process.Kill(); [void]$process.WaitForExit(5000) } catch { }
        }
    }
    Remove-Item Env:WUWA_NATIVE_DATA_ROOT -ErrorAction SilentlyContinue
    for ($attempt = 0; $attempt -lt 10 -and (Test-Path $temp); $attempt++) {
        try { Remove-Item $temp -Recurse -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 200 }
    }
    if (Test-Path $temp) { Write-Warning "Rotation smoke temp directory could not be removed immediately: $temp" }
}
