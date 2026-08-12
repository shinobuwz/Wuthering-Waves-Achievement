[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputDirectory = '',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$nativeRoot = Join-Path $repoRoot 'native'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $nativeRoot "publish/$RuntimeIdentifier"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$allowedPublishRoot = [System.IO.Path]::GetFullPath((Join-Path $nativeRoot 'publish'))
$allowedPrefix = $allowedPublishRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($OutputDirectory -eq $allowedPublishRoot -or -not $OutputDirectory.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be a child of $allowedPublishRoot."
}
if (Test-Path $OutputDirectory) {
    $hasPackageMarker = Test-Path (Join-Path $OutputDirectory 'package-manifest.json')
    $hasPublishedExe = Test-Path (Join-Path $OutputDirectory 'Wuwa.App.exe')
    if (-not $hasPackageMarker -and -not $hasPublishedExe) { throw "Refusing to replace an existing non-package directory: $OutputDirectory" }
}

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

Push-Location $nativeRoot
try {
    $expectedSdk = (Get-Content (Join-Path $nativeRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $actualSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $expectedSdk) { throw "Expected .NET SDK $expectedSdk but resolved $actualSdk." }
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'A 64-bit Windows host is required.' }

    & (Join-Path $PSScriptRoot 'build-native-ocr.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Native OCR build failed.' }

    Invoke-Checked 'dotnet' @('restore', 'WutheringWavesAchievement.sln')
    if (-not $SkipTests) { Invoke-Checked 'dotnet' @('test', 'WutheringWavesAchievement.sln', '-c', $Configuration, '--no-restore') }
    Invoke-Checked 'dotnet' @('restore', 'src/Wuwa.App/Wuwa.App.csproj', '-r', $RuntimeIdentifier)

    $temporaryOutput = Join-Path ([System.IO.Path]::GetTempPath()) ("wuwa-native-publish-" + [Guid]::NewGuid().ToString('N'))
    New-Item $temporaryOutput -ItemType Directory | Out-Null
    Invoke-Checked 'dotnet' @('publish', 'src/Wuwa.App/Wuwa.App.csproj', '-c', $Configuration, '-r', $RuntimeIdentifier, '--self-contained', 'true', '-p:PublishSingleFile=true', '--no-restore', '-o', $temporaryOutput)

    $ocrOutput = Join-Path $nativeRoot "ocr/build/$Configuration"
    $packageOcrRoot = Join-Path $temporaryOutput 'ocr'
    $modelTarget = Join-Path $packageOcrRoot 'models/ppocrv5'
    New-Item $packageOcrRoot -ItemType Directory -Force | Out-Null
    New-Item (Join-Path $modelTarget 'det') -ItemType Directory -Force | Out-Null
    New-Item (Join-Path $modelTarget 'rec') -ItemType Directory -Force | Out-Null
    New-Item (Join-Path $modelTarget 'cls') -ItemType Directory -Force | Out-Null

    foreach ($file in @('Wuwa.Ocr.Native.dll', 'onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'opencv_world4120.dll')) {
        $source = Join-Path $ocrOutput $file
        if (-not (Test-Path $source)) { throw "Required native OCR file was not produced: $source" }
        Copy-Item $source $packageOcrRoot -Force
    }
    Copy-Item (Join-Path $repoRoot 'onnxocr/models/ppocrv5/det/det.onnx') (Join-Path $modelTarget 'det/det.onnx') -Force
    Copy-Item (Join-Path $repoRoot 'onnxocr/models/ppocrv5/rec/rec.onnx') (Join-Path $modelTarget 'rec/rec.onnx') -Force
    Copy-Item (Join-Path $repoRoot 'onnxocr/models/ppocrv5/cls/cls.onnx') (Join-Path $modelTarget 'cls/cls.onnx') -Force
    Copy-Item (Join-Path $repoRoot 'onnxocr/models/ppocrv5/ppocrv5_dict.txt') $modelTarget -Force
    Copy-Item (Join-Path $nativeRoot 'ocr/THIRD_PARTY.md') $packageOcrRoot -Force

    foreach ($required in @('Wuwa.App.exe', 'resources/base_achievements.json', 'resources/category_config.json', 'ocr/Wuwa.Ocr.Native.dll')) {
        if (-not (Test-Path (Join-Path $temporaryOutput $required))) { throw "Published package is missing $required." }
    }
    Get-ChildItem $temporaryOutput -Recurse -File -Filter '*.pdb' | Remove-Item -Force
    $forbidden = Get-ChildItem $temporaryOutput -Recurse -File | Where-Object { $_.Extension -eq '.py' -or $_.Name -like 'user_progress_*.json' -or $_.Name -eq 'config.json' }
    if ($forbidden) { throw "Published package contains forbidden files: $($forbidden.FullName -join ', ')" }

    $manifest = Get-ChildItem $temporaryOutput -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            Path = $_.FullName.Substring($temporaryOutput.Length + 1).Replace('\', '/')
            Size = $_.Length
            Sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $temporaryOutput 'package-manifest.json') -Encoding UTF8

    New-Item (Split-Path -Parent $OutputDirectory) -ItemType Directory -Force | Out-Null
    $backupOutput = $OutputDirectory + '.previous-' + [Guid]::NewGuid().ToString('N')
    if (Test-Path $OutputDirectory) { Move-Item $OutputDirectory $backupOutput }
    try {
        Move-Item $temporaryOutput $OutputDirectory
        if (Test-Path $backupOutput) { Remove-Item $backupOutput -Recurse -Force }
    }
    catch {
        if (-not (Test-Path $OutputDirectory) -and (Test-Path $backupOutput)) { Move-Item $backupOutput $OutputDirectory }
        throw
    }
    Write-Host "Native package with OCR assets: $OutputDirectory"
}
finally {
    Pop-Location
    if ($temporaryOutput -and (Test-Path $temporaryOutput)) { Remove-Item $temporaryOutput -Recurse -Force }
}
