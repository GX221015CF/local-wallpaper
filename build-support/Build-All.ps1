$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath (Join-Path $root '.owned-build-stage'))){throw 'Use scripts/Build-Delivery.ps1 to build in an isolated stage.'}
# Build only the self-owned host; third-party runtime files are never copied.
 $selfHostPath=Join-Path $root 'build\runtime\Desktop317495Host-refined.exe'
 $manifest=Join-Path $root 'build\runtime-manifest.json'
 $receiptPath=Join-Path $root 'evidence\host-build.json'
 if((Test-Path -LiteralPath $selfHostPath) -or (Test-Path -LiteralPath $manifest) -or (Test-Path -LiteralPath $receiptPath)){
  if(!(Test-Path -LiteralPath $selfHostPath -PathType Leaf) -or !(Test-Path -LiteralPath $manifest -PathType Leaf) -or !(Test-Path -LiteralPath $receiptPath -PathType Leaf)){throw 'Partial existing self-owned host build; preserve it and inspect identities before continuing.'}
  $receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
  $actualHost=(Get-FileHash -LiteralPath $selfHostPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if($actualHost -cne [string]$receipt.hostSha256){throw 'Existing self-owned host does not match its build receipt.'}
  if((Get-FileHash -LiteralPath (Join-Path $root 'rules\runtime-components.json') -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$receipt.componentContractSha256){throw 'Existing component contract does not match the self-owned host receipt.'}
  $published=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
  if(@($published.files).Count -ne 1 -or $published.files[0].name -cne 'Desktop317495Host-refined.exe' -or $published.files[0].sha256 -cne $actualHost){throw 'Existing self-owned runtime manifest identity mismatch.'}
  foreach($source in $receipt.sourceFiles){$sourcePath=Join-Path $root ([string]$source.path);if(!(Test-Path -LiteralPath $sourcePath -PathType Leaf) -or (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne [string]$source.sha256){throw "Self-owned host source differs from receipt: $($source.path)"}}
  Write-Output 'Existing self-owned host, contract, manifest and source receipt match; reusing without overwrite.'
 }else{& (Join-Path $PSScriptRoot 'Build-Host.ps1');if(-not $?){throw 'Self-owned host build script failed.'}}
& (Join-Path $PSScriptRoot 'Build.ps1');if(-not $?){throw 'Application build script failed.'}
$out=Join-Path $root 'build\WallpaperLocalTool.exe'
if(Test-Path -LiteralPath $out){throw 'Existing tool build preserved'}
$files=Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter *.cs|Where-Object {$_.Name -notmatch 'Smoke|OfflineTest|TestScenario|MovieSmoke'}
$arguments=@('/nologo','/target:exe','/main:LocalTool','/platform:x86','/optimize+','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Core.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',('/out:'+$out))
$arguments+=$files.FullName
$arguments+=Join-Path $root 'shared\DailyLog.cs';$arguments+=Join-Path $root 'shared\AppVisuals.cs';$arguments+=('/win32icon:'+(Join-Path $root 'assets\LocalWallpaper.ico'));$arguments+=('/resource:'+(Join-Path $root 'assets\LocalWallpaper.ico')+',LocalWallpaper.ico')
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe') @arguments
$toolExit=$LASTEXITCODE
if($toolExit -ne 0){throw "Tool build failed with compiler exit $toolExit"}
& (Join-Path $PSScriptRoot 'Verify-Runtime.ps1')
if(-not $?){throw 'Runtime verification failed after tool build.'}
Write-Output $out

$out=Join-Path $root 'build\WallpaperVersionTool.exe'
if(Test-Path -LiteralPath $out){throw 'Existing version tool preserved'}
$arguments=@('/nologo','/target:exe','/main:VersionTool','/platform:x86','/optimize+','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Core.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',('/out:'+$out))
$arguments+=$files.FullName
$arguments+=Join-Path $root 'shared\DailyLog.cs';$arguments+=Join-Path $root 'shared\AppVisuals.cs';$arguments+=('/win32icon:'+(Join-Path $root 'assets\LocalWallpaper.ico'));$arguments+=('/resource:'+(Join-Path $root 'assets\LocalWallpaper.ico')+',LocalWallpaper.ico')
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe') @arguments
$versionToolExit=$LASTEXITCODE
if($versionToolExit -ne 0){throw "Version tool build failed with compiler exit $versionToolExit"}
$launcher=Join-Path $root 'build\LocalWallpaperLauncher.exe'
if(Test-Path -LiteralPath $launcher){throw 'Existing launcher preserved'}
$launcherArgs=@('/target:winexe',('/out:'+$launcher))+@($arguments | Where-Object {$_ -notlike '/out:*' -and $_ -notlike '/target:*'})
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe') @launcherArgs
$launcherExit=$LASTEXITCODE
if($launcherExit -ne 0){throw "Launcher build failed with compiler exit $launcherExit"}
& (Join-Path $PSScriptRoot 'Write-Release.ps1')
if(-not $?){throw 'Release manifest generation or verification failed.'}
