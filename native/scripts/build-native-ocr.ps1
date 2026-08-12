[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OnnxRuntimeVersion = '1.22.1',
    [string]$OpenCvVersion = '4.12.0',
    [string]$OpenCvSha256 = 'b753b14d880b9bc8d89d6acd3b665c040baec0211078435432fcae117db707af',
    [switch]$SkipTests,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ocrRoot = Join-Path $repoRoot 'native/ocr'
$buildRoot = Join-Path $ocrRoot 'build'
$modelPath = Join-Path $repoRoot 'onnxocr/models/ppocrv5/rec/rec.onnx'
$detectionModelPath = Join-Path $repoRoot 'onnxocr/models/ppocrv5/det/det.onnx'
$classifierModelPath = Join-Path $repoRoot 'onnxocr/models/ppocrv5/cls/cls.onnx'
$dictionaryPath = Join-Path $repoRoot 'onnxocr/models/ppocrv5/ppocrv5_dict.txt'
$packageRoot = Join-Path $env:USERPROFILE ".nuget/packages/microsoft.ml.onnxruntime/$OnnxRuntimeVersion"
$dependencyRoot = Join-Path $env:LOCALAPPDATA 'WuwaNativeDeps'
$openCvArchive = Join-Path $dependencyRoot "opencv-$OpenCvVersion-windows.exe"
$openCvExtractRoot = Join-Path $dependencyRoot "opencv-$OpenCvVersion"
$openCvRoot = Join-Path $openCvExtractRoot 'opencv/build'

if ($Clean -and (Test-Path $buildRoot)) {
    Remove-Item $buildRoot -Recurse -Force
}

if (-not (Test-Path (Join-Path $packageRoot 'build/native/include/onnxruntime_cxx_api.h'))) {
    $restoreRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'wuwa-onnxruntime-restore'
    New-Item $restoreRoot -ItemType Directory -Force | Out-Null
    $projectPath = Join-Path $restoreRoot 'Restore.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Microsoft.ML.OnnxRuntime" Version="$OnnxRuntimeVersion" /></ItemGroup>
</Project>
"@ | Set-Content $projectPath -Encoding UTF8
    dotnet restore $projectPath
}

if (-not (Test-Path (Join-Path $openCvRoot 'OpenCVConfig.cmake'))) {
    New-Item $dependencyRoot -ItemType Directory -Force | Out-Null
    if (-not (Test-Path $openCvArchive)) {
        $downloadUrl = "https://github.com/opencv/opencv/releases/download/$OpenCvVersion/opencv-$OpenCvVersion-windows.exe"
        & curl.exe --fail --location --retry 3 --connect-timeout 30 --ssl-no-revoke --output $openCvArchive $downloadUrl
        if ($LASTEXITCODE -ne 0) { throw "OpenCV download failed with exit code $LASTEXITCODE." }
    }
    $actualHash = (Get-FileHash $openCvArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $OpenCvSha256.ToLowerInvariant()) {
        throw "OpenCV archive hash mismatch. Expected $OpenCvSha256, received $actualHash."
    }
    if (Test-Path $openCvExtractRoot) { Remove-Item $openCvExtractRoot -Recurse -Force }
    New-Item $openCvExtractRoot -ItemType Directory -Force | Out-Null
    & $openCvArchive "-o$openCvExtractRoot" -y | Out-Null
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio Installer vswhere.exe was not found.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio 2022 with the Desktop development with C++ workload is required.' }
$cmake = Join-Path $visualStudio 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
if (-not (Test-Path $cmake)) { throw "CMake was not found under $visualStudio." }

& $cmake -S $ocrRoot -B $buildRoot -G 'Visual Studio 17 2022' -A x64 `
    "-DONNXRUNTIME_ROOT=$packageRoot" `
    "-DOpenCV_DIR=$openCvRoot" `
    "-DWUWA_OCR_REC_MODEL=$modelPath" `
    "-DWUWA_OCR_DET_MODEL=$detectionModelPath" `
    "-DWUWA_OCR_CLS_MODEL=$classifierModelPath" `
    "-DWUWA_OCR_DICTIONARY=$dictionaryPath"
& $cmake --build $buildRoot --config $Configuration --parallel
if (-not $SkipTests) {
    & $cmake --build $buildRoot --config $Configuration --target RUN_TESTS
}

$output = Join-Path $buildRoot $Configuration
Write-Host "Native OCR output: $output"
