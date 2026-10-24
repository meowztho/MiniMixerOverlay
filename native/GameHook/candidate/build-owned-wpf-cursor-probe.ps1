$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\OwnedSphereCursor'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select -First 1
if(!$vs){throw 'MSVC missing'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'owned_wpf_cursor_router_probe.cpp'
$exe=Join-Path $out 'owned_wpf_cursor_router_probe.exe'
$obj=Join-Path $out 'owned_wpf_cursor_router_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Cursor decision compile failed'}
& $exe
if($LASTEXITCODE -ne 0){throw "Cursor decision test failed exit=$LASTEXITCODE"}
