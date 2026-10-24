$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\LiveWpfIpcClient'
New-Item $out -ItemType Directory -Force | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'live_wpf_ipc_probe.cpp'
$obj=Join-Path $out 'live_wpf_ipc_probe.obj'
$exe=Join-Path $out 'live_wpf_ipc_probe.exe'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /DMINIMIXER_OWNED_IPC_TEST_ONLY "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'WPF IPC client C++ build failed'}
Write-Output "WPF_IPC_CLIENT_READY=$exe"
