$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath (Join-Path $root '.owned-build-stage'))){throw 'Use scripts/Build-Delivery.ps1 to build in an isolated stage.'}
$evidence=Join-Path $root 'evidence'
New-Item -ItemType Directory -Path $evidence -Force|Out-Null
$contract=Join-Path $root 'rules\runtime-components.json'
$contractHash=(Get-FileHash -LiteralPath $contract -Algorithm SHA256).Hash.ToLowerInvariant()
$contractObject=Get-Content -LiteralPath $contract -Raw|ConvertFrom-Json
$commonText=[IO.File]::ReadAllText((Join-Path $root 'src\Common.cs'))
$deliveryText=[IO.File]::ReadAllText((Join-Path $root 'src\RuntimeDelivery.cs'))
$verifyText=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Verify-Runtime.ps1'))
$commonHost=[regex]::Match($commonText,'HostHash="([a-f0-9]{64})"')
$deliveryContract=[regex]::Match($deliveryText,'ContractHash="([a-f0-9]{64})"')
$verifyContract=[regex]::Match($verifyText,"-cne '([a-f0-9]{64})'")
$hostRow=@($contractObject.files|Where-Object{$_.name -ceq 'Desktop317495Host-refined.exe'})
if(!$commonHost.Success -or !$deliveryContract.Success -or !$verifyContract.Success -or $contractHash -cne $deliveryContract.Groups[1].Value -or $contractHash -cne $verifyContract.Groups[1].Value -or @($contractObject.files).Count -ne 24 -or $hostRow.Count -ne 1 -or [string]$hostRow[0].sha256 -cne $commonHost.Groups[1].Value){throw 'Existing source pins and 24-row component contract do not agree; preserve and inspect revision.'}
$buildRoot=Join-Path $root 'build'
$runtime=Join-Path $buildRoot 'runtime'
$manifest=Join-Path $buildRoot 'runtime-manifest.json'
$destination=Join-Path $runtime 'Desktop317495Host-refined.exe'
if((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $manifest)){throw 'Existing build outputs are preserved; this script will not overwrite them.'}
$isolation=Join-Path $root 'tests\isolation'
New-Item -ItemType Directory -Force -Path $runtime,$isolation | Out-Null
$stage=Join-Path $isolation ('host-build-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$output=Join-Path $stage 'Desktop317495Host-refined.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$compilerArgs=@('/nologo','/target:winexe','/platform:x86','/optimize+','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',('/out:'+$output))
foreach($file in @('EngineCore.cs','HostShutdown.cs','DesktopLease.cs','DesktopHost.cs','PreviewToolbar.cs')){$compilerArgs+=Join-Path $root ('host_src\'+$file)}
$compilerArgs+=Join-Path $root 'shared\DailyLog.cs';$compilerArgs+=Join-Path $root 'shared\AppVisuals.cs';$compilerArgs+=('/win32icon:'+(Join-Path $root 'assets\LocalWallpaper.ico'));$compilerArgs+=('/resource:'+(Join-Path $root 'assets\LocalWallpaper.ico')+',LocalWallpaper.ico')
& $compiler @compilerArgs
$compilerExit=$LASTEXITCODE
if($compilerExit -ne 0){throw "Self-owned host compilation failed with exit $compilerExit."}
$hostHash=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
$hostRow=@($contractObject.files|Where-Object{$_.name -ceq 'Desktop317495Host-refined.exe'})
if($hostRow.Count -ne 1){throw 'Expected one self-owned host row.'}
$oldHostHash=$hostRow[0].sha256;$hostRow[0].sha256=$hostHash
$utf8=New-Object Text.UTF8Encoding($false)
$newContractTemp=$contract+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
$contractBackup=$contract+'.'+[Guid]::NewGuid().ToString('N')+'.bak'
try{[IO.File]::WriteAllText($newContractTemp,($contractObject|ConvertTo-Json -Depth 8),$utf8);$verified=Get-Content -LiteralPath $newContractTemp -Raw|ConvertFrom-Json;if(@($verified.files).Count -ne 24){throw 'Contract write verification failed'};[IO.File]::Replace($newContractTemp,$contract,$contractBackup);if(Test-Path -LiteralPath $contractBackup){Remove-Item -LiteralPath $contractBackup -Force}}finally{if(Test-Path -LiteralPath $newContractTemp){Remove-Item -LiteralPath $newContractTemp -Force};if(Test-Path -LiteralPath $contractBackup){Remove-Item -LiteralPath $contractBackup -Force}}
$newContractHash=(Get-FileHash -LiteralPath $contract -Algorithm SHA256).Hash.ToLowerInvariant()
foreach($relative in @('src\Common.cs','src\RuntimeDelivery.cs','scripts\Verify-Runtime.ps1')){
 $path=Join-Path $root $relative;$content=[IO.File]::ReadAllText($path).Replace($oldHostHash,$hostHash).Replace($contractHash,$newContractHash);[IO.File]::WriteAllText($path,$content,$utf8)
}
[IO.File]::Copy($output,$destination,$false)
if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hostHash){throw 'Published self-host copy identity mismatch.'}
if(-not ([IO.Path]::GetFullPath($stage).StartsWith(([IO.Path]::GetFullPath($isolation).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase))){throw 'Refusing to clean host staging outside tests/isolation.'}
Remove-Item -LiteralPath $stage -Recurse -Force
if(Test-Path -LiteralPath $stage){throw 'Host compiler staging directory was not removed after successful publication.'}
$sources=@();foreach($file in @('EngineCore.cs','HostShutdown.cs','DesktopLease.cs','DesktopHost.cs','PreviewToolbar.cs')){$p=Join-Path $root ('host_src\'+$file);$sources+=[ordered]@{path=('host_src/'+$file);bytes=(Get-Item -LiteralPath $p).Length;sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}};$p=Join-Path $root 'shared\DailyLog.cs';$sources+=[ordered]@{path='shared/DailyLog.cs';bytes=(Get-Item -LiteralPath $p).Length;sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
$sources+=@('shared/AppVisuals.cs','assets/LocalWallpaper.ico'|ForEach-Object{$p=Join-Path $root $_;[ordered]@{path=$_;bytes=(Get-Item -LiteralPath $p).Length;sha256=(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}})
$receipt=[ordered]@{schemaVersion=1;builtAt=(Get-Date).ToString('o');hostSha256=$hostHash;previousPinnedHostSha256=$oldHostHash;componentContractSha256=$newContractHash;sourceFiles=$sources;stagingCleaned=$true;externalComponentsBundled=$false;externalComponentsStaticOrLoadTested=$false;buildScope='self-owned host only; x86; no player launched'}
[IO.File]::WriteAllText((Join-Path $root 'evidence\host-build.json'),($receipt|ConvertTo-Json -Depth 8),$utf8)
$one=[ordered]@{files=@([ordered]@{name='Desktop317495Host-refined.exe';sha256=$hostHash})}
[IO.File]::WriteAllText($manifest,($one|ConvertTo-Json -Depth 5),$utf8)
& (Join-Path $PSScriptRoot 'Verify-Runtime.ps1')
if(-not $?){throw 'Self-owned runtime verification failed.'}
Write-Output ('Built own host; SHA-256='+$hostHash+'; external component binaries bundled=0')
