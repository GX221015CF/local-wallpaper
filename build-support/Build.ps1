param([string]$OutputName='LocalWallpaper.exe',[switch]$TestBuild,[string]$TestOutputDirectory='tests\bin')
$ErrorActionPreference='Stop'
if($OutputName -notmatch '^[A-Za-z0-9_-]+\.exe$'){throw 'Use a safe new name'}
$root=Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath (Join-Path $root '.owned-build-stage'))){throw 'Use scripts/Build-Delivery.ps1 to build in an isolated stage.'}
$folder=if($TestBuild){
 $candidate=[IO.Path]::GetFullPath((Join-Path $root $TestOutputDirectory));$isolation=[IO.Path]::GetFullPath((Join-Path $root 'tests\isolation')).TrimEnd('\')+'\'
 if(!$candidate.StartsWith($isolation,[StringComparison]::OrdinalIgnoreCase)){throw 'Test output must stay under tests\isolation.'}
 $candidate.Substring($root.Length+1)
}else{'build'}
$output=Join-Path $root ($folder+'\'+$OutputName)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
if(Test-Path -LiteralPath $output){throw 'Existing build preserved'}
if(!$TestBuild){& (Join-Path $PSScriptRoot 'Verify-Runtime.ps1') -Generate}
$arguments=@('/nologo','/target:winexe','/main:Entry','/platform:x86','/optimize+','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Core.dll','/r:System.Security.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',('/out:'+$output))
$arguments+=('/win32icon:'+(Join-Path $root 'assets\LocalWallpaper.ico'));$arguments+=('/resource:'+(Join-Path $root 'assets\LocalWallpaper.ico')+',LocalWallpaper.ico');$arguments+=Join-Path $root 'shared\AppVisuals.cs'
if($TestBuild){$arguments+='/define:TEST_BUILD'}
if($TestBuild){$arguments+=Join-Path $root 'src\HostFailureReplaySmoke.cs';$arguments+=Join-Path $root 'src\CorePlaybackSmoke.cs';$arguments+=Join-Path $root 'src\RuntimeProvisioningSmoke.cs';$arguments+=Join-Path $root 'src\SourceLocationSmoke.cs';$arguments+=Join-Path $root 'src\SourceRecoverySmoke.cs';$arguments+=Join-Path $root 'src\DataHomeUiSmoke.cs';$arguments+=Join-Path $root 'src\DataHomeSmoke.cs';$arguments+=Join-Path $root 'src\SubmitSmoke.cs';$arguments+=Join-Path $root 'src\DiagnosticSmoke.cs';$arguments+=Join-Path $root 'src\NameFlowSmoke.cs';$arguments+=Join-Path $root 'src\NameDiagnosticsSmoke.cs'}
foreach($name in @('Common.cs','DataLocation.cs','DataMigration.cs','VersionStore.cs','FirstUseSetup.cs','SourceLocation.cs','FaultRecord.cs','DiagnosticBundle.cs','FaultHandoffUi.cs','FaultController.cs','StartupHandoff.cs','OfficialNameStore.cs','NameFlowUi.cs','PreviewFrame.cs','PreviewCovers.cs','PreviewCoverUi.cs','CandidateChecks.cs','RuntimeDelivery.cs','RuntimeProvisioning.cs','RuntimePresentation.cs','RuntimeDiscovery.cs','RuntimePreparationUi.cs','PersonalResourceImport.cs','SingleInstance.cs','LoginRestore.cs','DailyFlowUi.cs','TrayControl.cs','TrayUi.cs','VideoPackage.cs','VideoFrameDecoder.cs','VideoCovers.cs','ResourceDiscovery.cs','DiscoveryUi.cs','EmptyStatePresentation.cs','EmptyStateUi.cs','OfficialNames.cs','NameUpdates.cs','RemovalUi.cs','V3Package.cs','PackageAnalysis.cs','ManagedCatalog.cs','CoverCache.cs','LocalImport.cs','ImportRegistration.cs','ImportDialog.cs','CwpPolicy.cs','ProcessProof.cs','LeaseWorker.cs','RuntimeInputs.cs','HostResources.cs','PlaybackFailure.cs','ControlWindow.cs','PlaybackPreparation.cs','PlaybackSwitch.cs','Lifecycle.cs','Entry.cs','WallpaperLibrary.cs','WallpaperNames.cs','MaterialControls.cs','SettingsUi.cs','RenameUi.cs','CoverCard.cs','PreviewConfirmation.cs','JobFence.cs','IncrementalLines.cs','ContinuousSafety.cs','DailyMetrics.cs','WorkerHeartbeat.cs','PolishedUi.cs','Theme.cs','AppSettings.cs','AudioUi.cs')){$arguments+=Join-Path $root ('src\'+$name)}
if($TestBuild){$firstUseRegression=Join-Path $root 'tests\FirstUseRegression.cs';if(Test-Path -LiteralPath $firstUseRegression -PathType Leaf){$arguments+=$firstUseRegression};$arguments+=Join-Path $root 'src\HeadlessDiagnosticSmoke.cs';$arguments+=Join-Path $root 'src\StartupDiagnosticSmoke.cs'}
if($TestBuild){$arguments+=Join-Path $root 'src\PreviewCandidateSmoke.cs';$arguments+=Join-Path $root 'src\PreviewControlsSmoke.cs';$arguments+=Join-Path $root 'src\TestScenario.cs';$arguments+=Join-Path $root 'src\OfflineTest.cs';$arguments+=Join-Path $root 'src\MovieSmoke.cs';$arguments+=Join-Path $root 'src\ImportSmoke.cs';$arguments+=Join-Path $root 'src\ImportFlowSmoke.cs';$arguments+=Join-Path $root 'src\CoverSmoke.cs';$arguments+=Join-Path $root 'src\Cover366Smoke.cs';$arguments+=Join-Path $root 'src\NameSmoke.cs';$arguments+=Join-Path $root 'src\DailyPolishSmoke.cs';$arguments+=Join-Path $root 'src\DiscoverySmoke.cs';$arguments+=Join-Path $root 'src\DailyFlowSmoke.cs';$arguments+=Join-Path $root 'src\WorkflowUiSmoke.cs';$arguments+=Join-Path $root 'src\PreviewCoverSmoke.cs';$arguments+=Join-Path $root 'src\PreviewCoverStoreSmoke.cs';$arguments+=Join-Path $root 'src\EmptyStateSmoke.cs';$arguments+=Join-Path $root 'src\CoverPathSmoke.cs'}
$arguments+=Join-Path $root 'shared\DailyLog.cs'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if($TestBuild -and $env:LOCALWALLPAPER_BUILD_TRACE){
 $tracePath=[IO.Path]::GetFullPath($env:LOCALWALLPAPER_BUILD_TRACE)
 $compilerHash=(Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant()
 $framework=Split-Path -Parent $compiler
 $inputPaths=@($compiler,$PSCommandPath)+@($arguments|Where-Object{$_ -notmatch '^/' -and (Test-Path -LiteralPath $_ -PathType Leaf)})
 foreach($arg in $arguments){if($arg -match '^/r:(.+)$'){$reference=$Matches[1];if(!(Test-Path -LiteralPath $reference -PathType Leaf)){$reference=Join-Path $framework $reference};if(Test-Path -LiteralPath $reference -PathType Leaf){$inputPaths+=$reference}}}
 $inputs=@($inputPaths|Sort-Object -Unique|ForEach-Object{$full=[IO.Path]::GetFullPath($_);[ordered]@{path=$full;bytes=(Get-Item -LiteralPath $full).Length;sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()}})
 $trace=[ordered]@{schemaVersion=1;capturedAt=(Get-Date).ToString('o');mode='TEST_BUILD';revision=(Split-Path $root -Leaf);buildScript=$PSCommandPath;buildScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant();compiler=$compiler;compilerSha256=$compilerHash;compilerFileVersion=(Get-Item -LiteralPath $compiler).VersionInfo.FileVersion;arguments=$arguments;inputs=$inputs;output=$output;outputSha256=$null;outputBytes=$null;result='compiler invocation prepared'}
 [IO.File]::WriteAllText($tracePath,($trace|ConvertTo-Json -Depth 10),(New-Object Text.UTF8Encoding($false)))
}
& $compiler @arguments
$compilerExit=$LASTEXITCODE
if($compilerExit -ne 0){throw "Build failed with compiler exit $compilerExit"}
if($TestBuild -and $env:LOCALWALLPAPER_BUILD_TRACE){$trace=Get-Content -LiteralPath $tracePath -Raw|ConvertFrom-Json;$trace.outputSha256=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant();$trace.outputBytes=(Get-Item -LiteralPath $output).Length;$trace.result='compiled';[IO.File]::WriteAllText($tracePath,($trace|ConvertTo-Json -Depth 10),(New-Object Text.UTF8Encoding($false)))}
if(!$TestBuild){& (Join-Path $PSScriptRoot 'Verify-Runtime.ps1');if(-not $?){throw 'Runtime identity verification failed.'}}
Write-Output $output
