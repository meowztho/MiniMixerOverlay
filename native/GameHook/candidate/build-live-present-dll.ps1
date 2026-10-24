$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$out=Join-Path $root '.tmp\NativeCandidate\LiveInGame'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dep=Join-Path $root 'native\third_party\minhook-1.3.4'
if(!(Test-Path (Join-Path $dep 'LICENSE.txt'))){throw 'Pinned MinHook dependency unavailable'}
$vswhere='C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)|Select-Object -First 1
if(!$vs){throw 'MSVC x64 compiler not installed'}
$vcvars=Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$src=Join-Path $dep 'src'
$includes=Join-Path $dep 'include'
$objects=@()
foreach($name in @('buffer','hook','trampoline')){
 $obj=Join-Path $out ("MinHook_"+$name+".obj")
 $file=Join-Path $src ($name+'.c')
 $cmd='call "'+$vcvars+'" >nul && cl /nologo /W3 /O2 /c /TC /I"'+$includes+'" "'+$file+'" /Fo:"'+$obj+'"'
 cmd /d /s /c $cmd
 if($LASTEXITCODE -ne 0){throw "Cannot compile MinHook $name"}
 $objects+='"'+$obj+'"'
}
$obj=Join-Path $out 'MinHook_hde64.obj'
$file=Join-Path $src 'hde\hde64.c'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /W3 /O2 /c /TC /I"'+$includes+'" "'+$file+'" /Fo:"'+$obj+'"'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Cannot compile MinHook hde64'}
$objects+='"'+$obj+'"'
$cpp=Join-Path $PSScriptRoot 'live_game_present_dll.cpp'
$obj=Join-Path $out 'live_game_present_dll.obj'
$binary=Join-Path $out 'MiniMixerLivePresent.x64.dll'
$cmd='call "'+$vcvars+'" >nul && cl /nologo /std:c++17 /EHsc /W3 /O2 /LD /I"'+$includes+'" "'+
      $cpp+'" /Fo:"'+$obj+'" /Fe:"'+$binary+'" '+
      ($objects -join ' ')+' /link kernel32.lib user32.lib dxgi.lib d3d11.lib d3d12.lib d3dcompiler.lib'
cmd /d /s /c $cmd
if($LASTEXITCODE -ne 0){throw 'Cannot compile own in-game Present tracer'}
$sha=(Get-FileHash $binary -Algorithm SHA256).Hash
$stableDir=Join-Path $out ('immutable\'+$sha)
New-Item -ItemType Directory -Force -Path $stableDir | Out-Null
$stable=Join-Path $stableDir ('MiniMixerLivePresent.'+$sha.Substring(0,16)+'.x64.dll')
if(Test-Path $stable){
    if((Get-FileHash $stable -Algorithm SHA256).Hash -ne $sha){throw 'Immutable DLL path corrupted'}
}else{
    Copy-Item -LiteralPath $binary -Destination $stable -ErrorAction Stop
}
Write-Output "LIVE_PRESENT_DLL_READY=$binary"
Write-Output "IMMUTABLE_DLL=$stable"
Write-Output "DLL_SHA256=$sha"
Write-Output "PINNED_DEPENDENCY=minhook-v1.3.4 BSD-2-Clause"
