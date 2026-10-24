$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$core=Join-Path $root 'native\GraphicsHookCore'
$mh=Join-Path $root 'native\third_party\minhook-1.3.4'
$out=Join-Path $root 'dist\GraphicsHookCorePortable'
$zip="$out.zip"
if((Test-Path $out) -or (Test-Path $zip)){throw 'Portable graphics-core artifact exists; never overwrite consumer copies'}
$include=Join-Path $out 'include'
$vendor=Join-Path $out 'vendor\minhook-1.3.4'
$tests=Join-Path $out 'tests'
New-Item -ItemType Directory -Force -Path $include,$vendor,$tests|Out-Null
Copy-Item -LiteralPath (Join-Path $core 'README.md') -Destination (Join-Path $out 'README.md')
Copy-Item -Path (Join-Path $core 'include\*') -Destination $include -Recurse
Copy-Item -Path (Join-Path $mh '*') -Destination $vendor -Recurse
Copy-Item -LiteralPath (Join-Path $core 'tests\standalone_contract_smoke.cpp') -Destination $tests
if(!(Test-Path (Join-Path $vendor 'LICENSE.txt'))){throw 'MinHook BSD license missing'}
$manifest=@(
 'GraphicsHookCorePortable, source revision: '+(& git -C $root rev-parse --short HEAD)
 'Public API: include/graphics_hook_core/'
 'Dependency: pinned MinHook v1.3.4 under vendor/minhook-1.3.4/'
 'Build: MSVC C++17 Windows SDK, DirectX11, DXGI, MinHook as an adapter dependency.'
 'Boundary: no injector, no game process selection, no third-party anti-cheat bypass.'
 'Integration: callback/adapter must implement its own explicit eligibility and synchronization.'
)
[System.IO.File]::WriteAllLines((Join-Path $out 'MANIFEST.txt'),$manifest)
$files=Get-ChildItem $out -Recurse -File
if(@($files|Where-Object{$_.Name -match 'n_overlay|injector_helper|goverlay'}).Count -ne 0){throw 'Prohibited old vendor artifact in reusable core'}
Compress-Archive -Path $out -DestinationPath $zip -CompressionLevel Optimal
$hash=(Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash
Write-Output ('PORTABLE_GRAPHICS_HOOK_CORE='+$zip)
Write-Output ('PORTABLE_SHA256='+$hash)
Write-Output ('PORTABLE_FILES='+$files.Count)
