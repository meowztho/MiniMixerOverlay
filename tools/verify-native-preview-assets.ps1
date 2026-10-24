param(
    [Parameter(Mandatory=$true)][string]$PackageDir,
    [Parameter(Mandatory=$true)][string]$SourceDll
)
$ErrorActionPreference='Stop'
$package=(Resolve-Path -LiteralPath $PackageDir).Path
$source=(Resolve-Path -LiteralPath $SourceDll).Path
$native=Join-Path $package 'native\GameHook\candidate\MiniMixerLivePresent.x64.dll'
$exe=Join-Path $package 'MiniMixerOverlay.App.exe'
$required=@(
    $exe, $native,
    (Join-Path $package 'MINHOOK-LICENSE.txt'),
    (Join-Path $package 'README_NATIVE_PREVIEW.txt')
)
foreach($file in $required){
    if(!(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Required native preview file missing: $file"
    }
}
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$minhookLicense=Join-Path $root 'native\third_party\minhook-1.3.4\LICENSE.txt'
$stagedLicense=Join-Path $package 'MINHOOK-LICENSE.txt'
if(!(Test-Path -LiteralPath $minhookLicense -PathType Leaf) -or
   (Get-FileHash $minhookLicense -Algorithm SHA256).Hash -ne
   (Get-FileHash $stagedLicense -Algorithm SHA256).Hash){
    throw 'MinHook license was missing, changed or not copied from pinned source'
}
$hash=(Get-FileHash $source -Algorithm SHA256).Hash
$packaged=(Get-FileHash $native -Algorithm SHA256).Hash
if($hash -ne $packaged){throw 'Native DLL SHA256 differs from independently built source'}
$stream=[IO.File]::OpenRead($native)
try {
    $reader=[IO.BinaryReader]::new($stream)
    $stream.Position=0x3c
    $pe=$reader.ReadInt32()
    if($pe -lt 0 -or $pe+6 -gt $stream.Length){throw 'Invalid PE offset'}
    $stream.Position=$pe
    if($reader.ReadUInt32() -ne 0x00004550){throw 'Invalid native PE signature'}
    if($reader.ReadUInt16() -ne 0x8664){throw 'Native DLL not x64'}
} finally { $stream.Dispose() }
$forbidden=@(
    'n_overlay.dll','n_overlay.x64.dll',
    'injector_helper.exe','injector_helper.x64.exe',
    'GOverlay-LICENSE.txt'
)
$files=Get-ChildItem -LiteralPath $package -Recurse -File
foreach($item in $files) {
    if($item.Name -in $forbidden -or
       $item.FullName -match '(?i)[\\/](legacy|goverlay-master|prebuilt)[\\/]'){
        throw "Forbidden archived vendor asset in active package: $($item.FullName)"
    }
}
if(@($files).Count -ne 4){throw "Unexpected preview payload: $(@($files).Count) files"}
Write-Output "PASS NATIVE_PREVIEW_NO_GOVERLAY files=4 dllSHA256=$hash x64=1 releaseStatus=UNVERIFIED"
