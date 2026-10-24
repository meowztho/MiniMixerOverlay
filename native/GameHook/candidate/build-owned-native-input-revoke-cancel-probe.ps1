$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\OwnedRevokeAndCancel'
New-Item -ItemType Directory -Force $out | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC not available'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'owned_native_input_revoke_cancel_probe.cpp'
$obj=Join-Path $out 'owned_native_input_revoke_cancel_probe.obj'
$exe=Join-Path $out 'owned_native_input_revoke_cancel_probe.exe'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /I"'+$PSScriptRoot+'" "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib d3d11.lib d3dcompiler.lib dxgi.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw "Native target revoke fixture compile failed"}
& $exe
if($LASTEXITCODE -ne 0){throw "Native target revoke fixture failed $LASTEXITCODE"}
