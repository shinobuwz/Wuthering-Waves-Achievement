[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OnnxRuntimeVersion = '1.22.1',
    [switch]$SkipTests,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ocrRoot = Join-Path $repoRoot 'native/ocr'
$buildRoot = Join-Path $ocrRoot 'build'
$modelPath = Join-Path $repoRoot 'onnxocr/models/ppocrv5/rec/rec.onnx'
$dictionaryPath = Join-Path $repoRoot 'onnxocr/models/ppocrv5/ppocrv5_dict.txt'
$packageRoot = Join-Path $env:USERPROFILE ".nuget/packages/microsoft.ml.onnxruntime/$OnnxRuntimeVersion"

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

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio Installer vswhere.exe was not found.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio 2022 with the Desktop development with C++ workload is required.' }
$cmake = Join-Path $visualStudio 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
if (-not (Test-Path $cmake)) { throw "CMake was not found under $visualStudio." }

& $cmake -S $ocrRoot -B $buildRoot -G 'Visual Studio 17 2022' -A x64 `
    "-DONNXRUNTIME_ROOT=$packageRoot" `
    "-DWUWA_OCR_REC_MODEL=$modelPath" `
    "-DWUWA_OCR_DICTIONARY=$dictionaryPath"
& $cmake --build $buildRoot --config $Configuration --parallel
if (-not $SkipTests) {
    & $cmake --build $buildRoot --config $Configuration --target RUN_TESTS
}

$output = Join-Path $buildRoot $Configuration
Write-Host "Native OCR output: $output"
