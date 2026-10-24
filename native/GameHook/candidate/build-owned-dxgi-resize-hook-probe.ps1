$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\OwnedDxgiResizeHook'
New-Item -ItemType Directory -Path $out -Force|Out-Null
& (Join-Path $PSScriptRoot 'prepare-live-hook-dependency.ps1')
$dep=Join-Path $root 'native\third_party\minhook-1.3.4'
if(!(Test-Path (Join-Path $dep 'include\MinHook.h'))){throw 'Versioned MinHook source unavailable'}
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC missing'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$objects=@()
foreach($name in @('buffer','hook','trampoline','hde\hde64')){
 $stem=($name -replace '[\\/]','_')
 $source=Join-Path (Join-Path $dep 'src') ($name+'.c')
 $obj=Join-Path $out ($stem+'.obj')
 $cmd='call "'+$vcvars+'" >nul && cl /nologo /W3 /O2 /TC /c /I"'+(Join-Path $dep 'include')+'" "'+$source+'" /Fo:"'+$obj+'"'
 cmd /d /s /c $cmd
 if($LASTEXITCODE -ne 0){throw "MinHook compilation failed $name"}
 $objects+='"'+$obj+'"'
}
$source=Join-Path $PSScriptRoot 'owned_dxgi_resize_hook_probe.cpp'
$obj=Join-Path $out 'owned_dxgi_resize_hook_probe.obj'
$exe=Join-Path $out 'owned_dxgi_resize_hook_probe.exe'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /I"'+(Join-Path $dep 'include')+'" "'+$source+'" /Fo:"'+$obj+'" /Fe:"'+$exe+'" '+
      ($objects -join ' ')+' /link user32.lib d3d12.lib dxgi.lib d3dcompiler.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Owned ResizeBuffers hook test C++ build failed'}
& $exe
if($LASTEXITCODE -ne 0){throw "Owned ResizeBuffers hook runtime failed with code $LASTEXITCODE"}
