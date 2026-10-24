$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\OwnedLayerCursor'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'owned_layer_cursor_regression.cpp'
$obj=Join-Path $out 'owned_layer_cursor_regression.obj'
$exe=Join-Path $out 'owned_layer_cursor_regression.exe'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Layer cursor build failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Layer cursor regression failed'}
