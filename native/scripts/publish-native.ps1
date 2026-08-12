[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputDirectory = '',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "native/publish/$RuntimeIdentifier"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

& (Join-Path $PSScriptRoot 'build-native-ocr.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Native OCR build failed.' }

$solution = Join-Path $repoRoot 'native/WutheringWavesAchievement.sln'
dotnet restore $solution
if (-not $SkipTests) {
    dotnet test $solution -c $Configuration --no-restore
}

$appProject = Join-Path $repoRoot 'native/src/Wuwa.App/Wuwa.App.csproj'
dotnet restore $appProject -r $RuntimeIdentifier
dotnet publish $appProject -c $Configuration -r $RuntimeIdentifier --self-contained true -p:PublishSingleFile=true --no-restore -o $OutputDirectory

$ocrOutput = Join-Path $repoRoot "native/ocr/build/$Configuration"
$packageOcrRoot = Join-Path $OutputDirectory 'ocr'
$modelTarget = Join-Path $packageOcrRoot 'models/ppocrv5'
New-Item $packageOcrRoot -ItemType Directory -Force | Out-Null
New-Item (Join-Path $modelTarget 'det') -ItemType Directory -Force | Out-Null
New-Item (Join-Path $modelTarget 'rec') -ItemType Directory -Force | Out-Null

foreach ($file in @('Wuwa.Ocr.Native.dll', 'onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'opencv_world4120.dll')) {
    $source = Join-Path $ocrOutput $file
    if (-not (Test-Path $source)) { throw "Required native OCR file was not produced: $source" }
    Copy-Item $source $packageOcrRoot -Force
}
Copy-Item (Join-Path $repoRoot 'onnxocr/models/ppocrv5/det/det.onnx') (Join-Path $modelTarget 'det/det.onnx') -Force
Copy-Item (Join-Path $repoRoot 'onnxocr/models/ppocrv5/rec/rec.onnx') (Join-Path $modelTarget 'rec/rec.onnx') -Force
Copy-Item (Join-Path $repoRoot 'onnxocr/models/ppocrv5/ppocrv5_dict.txt') $modelTarget -Force
Copy-Item (Join-Path $repoRoot 'native/ocr/THIRD_PARTY.md') $packageOcrRoot -Force

Write-Host "Native package with OCR assets: $OutputDirectory"
