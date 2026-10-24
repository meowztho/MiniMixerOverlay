param(
    [string]$OutputDirectory = ""
)
$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$source = Join-Path $PSScriptRoot 'native_candidate.cpp'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot '.tmp\NativeCandidate'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio C++ Build Tools not installed' }
$vs = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath) | Select-Object -First 1
if (-not $vs) { throw 'No MSVC toolchain found' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path $vcvars)) { throw 'MSVC vcvars64.bat not found' }
$binary = Join-Path $OutputDirectory 'native_candidate.exe'
$obj = Join-Path $OutputDirectory 'native_candidate.obj'
$cmd = 'call "' + $vcvars + '" >nul && cl /nologo /std:c++17 /EHsc /W3 /O2 "' +
    $source + '" /Fo:"' + $obj + '" /Fe:"' + $binary + '" /link user32.lib gdi32.lib d3d11.lib d3d12.lib d3dcompiler.lib dxgi.lib'
cmd.exe /d /s /c $cmd
if ($LASTEXITCODE -ne 0) { throw "Native candidate build failed: $LASTEXITCODE" }
Write-Output "CANDIDATE_READY=$binary"
Write-Output "SOURCE_SHA256=$((Get-FileHash $source -Algorithm SHA256).Hash)"
