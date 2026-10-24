$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\LiveInGame'
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC not found'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'owned_user32_hook_probe.cpp'
$exe=Join-Path $out 'owned_user32_hook_probe.exe'
$obj=Join-Path $out 'owned_user32_hook_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /W4 /WX /EHsc /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link kernel32.lib user32.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Failed to build owned user32 probe'}
$dll=Join-Path $out 'MiniMixerLivePresent.x64.dll'
if(!(Test-Path $dll)){throw 'Missing experimental DLL'}
& $exe $dll
if($LASTEXITCODE -ne 0){throw "Windows hook test failed with $LASTEXITCODE"}
