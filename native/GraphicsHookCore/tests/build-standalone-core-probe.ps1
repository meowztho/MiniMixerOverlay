$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\GraphicsHookCore\Standalone'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC not installed'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $PSScriptRoot 'standalone_contract_smoke.cpp'
$exe=Join-Path $out 'standalone_contract_smoke.exe'
$obj=Join-Path $out 'standalone_contract_smoke.obj'
$include=Join-Path $root 'native\GraphicsHookCore\include'
$minhook=Join-Path $root 'native\third_party\minhook-1.3.4\include'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /I"'+$include+'" /I"'+$minhook+'" "'+$src+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" /link d3d11.lib dxgi.lib'
cmd.exe /d /s /c $cmd
if($LASTEXITCODE-ne 0){throw 'Standalone consumer compilation failed'}
& $exe
if($LASTEXITCODE-ne 0){throw "Standalone consumer smoke failed: $LASTEXITCODE"}
