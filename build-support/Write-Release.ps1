$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath (Join-Path $root '.owned-build-stage'))){throw 'Use scripts/Build-Delivery.ps1 to build in an isolated stage.'}
& (Join-Path $PSScriptRoot 'Verify-Runtime.ps1')
$files=@()
foreach($rel in @('build/LocalWallpaperLauncher.exe','build/LocalWallpaper.exe','build/WallpaperLocalTool.exe','build/WallpaperVersionTool.exe','build/runtime-manifest.json','rules/runtime-components.json','rules/v3-structure-families.json')){
 $p=Join-Path $root $rel
 $files+=@{Path=$rel;Bytes=(Get-Item -LiteralPath $p).Length;Sha256=(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
}
$out=Join-Path $root 'build/release.json'
if(Test-Path -LiteralPath $out){throw 'Release manifest already exists; old build preserved'}
$r=@{SchemaVersion=2;MinDataFormat=1;MaxDataFormat=1;Version='0.1.37-import-progress-internal-r37';BindingPolicy='per-user-data-location.json-not-in-release-identity';ExternalRuntime='user-selected local install root; identity checked against pinned 23-component contract; no external binaries in package';Files=$files}
$tmp=$out+'.tmp'
try{[IO.File]::WriteAllText($tmp,($r|ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding($false)));[IO.File]::Move($tmp,$out)}finally{if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp}}
& (Join-Path $root 'build/WallpaperVersionTool.exe') verify $root
$verifyExit=$LASTEXITCODE
if($verifyExit -ne 0){throw "Complete release verification failed with exit $verifyExit"}
