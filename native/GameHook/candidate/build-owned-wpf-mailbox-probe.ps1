$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\OwnedWpfMailbox'
New-Item -ItemType Directory -Path $out -Force|Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$source=Join-Path $PSScriptRoot 'owned_wpf_frame_mailbox_probe.cpp'
$obj=Join-Path $out 'owned_wpf_frame_mailbox_probe.obj'
$exe=Join-Path $out 'owned_wpf_frame_mailbox_probe.exe'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$source+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link user32.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Mailbox C++ compile failed'}
& $exe
if($LASTEXITCODE -ne 0){throw "Win32 mailbox test failed exit $LASTEXITCODE"}
