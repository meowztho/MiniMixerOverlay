$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\OwnedLifetime'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC x64 unavailable'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'owned_native_lifetime_probe.cpp'
$exe=Join-Path $out 'owned_native_lifetime_probe.exe'
$obj=Join-Path $out 'owned_native_lifetime_probe.obj'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'"'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Native lifetime probe compile failed'}
& $exe
if($LASTEXITCODE -ne 0){throw "Native lifetime probe failed, exit=$LASTEXITCODE"}
