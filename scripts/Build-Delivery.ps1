param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$artifactRoot = Join-Path $root 'artifacts'
$run = Join-Path $artifactRoot ('build-' + (Get-Date).ToString('yyyyMMdd-HHmmssfff'))
if (Test-Path -LiteralPath $run) { throw 'Existing build attempt preserved.' }
$stage = Join-Path $run 'source'
New-Item -ItemType Directory -Path $stage | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)

function Identity([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    [ordered]@{ path=$item.FullName; bytes=$item.Length; sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
function Save([string]$Path, $Value) {
    if (Test-Path -LiteralPath $Path) { throw "Existing receipt preserved: $Path" }
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12), $utf8)
}

$canonical = @()
foreach ($directory in @('src','host_src','shared','assets','rules','build-support','scripts','docs')) {
    $canonical += @(Get-ChildItem -LiteralPath (Join-Path $root $directory) -File -Recurse | ForEach-Object { Identity $_.FullName })
}
$canonical += @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -File | ForEach-Object { Identity $_.FullName })
$canonical += Identity (Join-Path $root 'README.md')
Save (Join-Path $run 'canonical-inputs-before.json') $canonical

foreach ($directory in @('src','host_src','shared','assets','rules','docs')) {
    Copy-Item -LiteralPath (Join-Path $root $directory) -Destination $stage -Recurse
}
Copy-Item -LiteralPath (Join-Path $root 'build-support') -Destination (Join-Path $stage 'scripts') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
[IO.File]::WriteAllText((Join-Path $stage '.owned-build-stage'), 'local-wallpaper-source-layout-v1', $utf8)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$shell = (Get-Command pwsh.exe -ErrorAction Stop).Source
$tools = @((Identity $compiler),(Identity $shell))
foreach ($reference in @('System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Core.dll','System.Security.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')) {
    $tools += Identity (Join-Path (Split-Path $compiler -Parent) $reference)
}
Save (Join-Path $run 'tools-before.json') $tools
Save (Join-Path $run 'stage-inputs-before.json') @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { Identity $_.FullName })
$steps = @()

function RunBuildStep([string]$Name, [string]$Script, [string[]]$ExtraArguments) {
    $log = Join-Path $run ($Name + '.log')
    $errorLog = Join-Path $run ($Name + '-stderr.log')
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $stage ('scripts/'+$Script))+'"')) + $ExtraArguments
    $process = Start-Process -FilePath $shell -ArgumentList $arguments -WorkingDirectory $stage -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $errorLog
    if (!$process.WaitForExit(60000)) { throw "$Name exceeded 60s; attempt preserved and not passed." }
    $code = $process.ExitCode
    $script:steps += [ordered]@{name=$Name;pid=$process.Id;exitCode=$code;normalExit=$true;forcedTermination=$false;log=$log;errorLog=$errorLog}
    $process.Dispose()
    if ($code -ne 0) { throw "$Name failed with exit $code. See saved logs." }
    Write-Output "$Name passed"
}

try {
    RunBuildStep 'release-build' 'Build-All.ps1' @()
    $stageSources = @(foreach($directory in @('src','host_src','shared','assets','rules','scripts','docs')) { Get-ChildItem -LiteralPath (Join-Path $stage $directory) -File -Recurse | ForEach-Object { Identity $_.FullName } })
    Save (Join-Path $run 'stage-inputs-after-host-pinning.json') $stageSources
    RunBuildStep 'package-build' 'Build-OfflinePackage.ps1' @()
    RunBuildStep 'package-verify' 'Verify-OfflinePackage.ps1' @('-EvidencePath',('"'+(Join-Path $run 'package-verification.json')+'"'))
    foreach ($file in $canonical) {
        if ((Identity $file.path).sha256 -cne $file.sha256) { throw "Canonical source changed during build: $($file.path)" }
    }
    $package = Join-Path $stage 'offline-package/LocalWallpaper-0.1.37-import-progress-internal-r37.zip'
    $receipt = [ordered]@{
        result='passed'; version='0.1.37-import-progress-internal-r37'; packagingRevision='source-layout.1'
        sourceRoot=$root; stageRoot=$stage; steps=$steps; sourceTreeUnchanged=$true
        outputs=@(Get-ChildItem -LiteralPath (Join-Path $stage 'build') -File -Recurse | ForEach-Object { Identity $_.FullName })
        package=(Identity $package); guiStarted=$false; thirdPartyEngineLoaded=$false; playbackStarted=$false
        note='R37 import progress and persistent cache verification built from maintained source in an owned stage. Runtime pins generated only in that stage. Original observed R30 package retained separately.'
    }
    Save (Join-Path $run 'build-result.json') $receipt
    Write-Output ('BUILD_RECEIPT=' + (Join-Path $run 'build-result.json'))
} catch {
    Save (Join-Path $run 'build-failure.json') ([ordered]@{result='failed';error=$_.Exception.Message;steps=$steps;stageRoot=$stage})
    throw
}
