$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if(!(Test-Path -LiteralPath (Join-Path $root '.owned-build-stage'))){throw 'Use scripts/Build-Delivery.ps1 to build in an isolated stage.'}
$outDir = Join-Path $root 'offline-package'
$packageName = 'LocalWallpaper-0.1.30-import-video-cover-internal-r30.zip'
$manifestName = 'manifest-import-video-cover-internal-r30.json'
$package = Join-Path $outDir $packageName
$outerManifest = Join-Path $outDir $manifestName
if (Test-Path -LiteralPath $package) { throw 'Existing R30 package is preserved.' }
if (Test-Path -LiteralPath $outerManifest) { throw 'Existing R30 manifest is preserved.' }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$build = Join-Path $root 'build'
if (Test-Path -LiteralPath (Join-Path $build 'data-location.json')) { throw 'User-specific data binding must not enter the package.' }
$versionTool = Join-Path $build 'WallpaperVersionTool.exe'
if (!(Test-Path -LiteralPath $versionTool -PathType Leaf)) { throw 'Build first; version verifier is missing.' }
& $versionTool verify $root
if ($LASTEXITCODE -ne 0) { throw 'Release verification failed.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$isolationRoot = [IO.Path]::GetFullPath((Join-Path $root 'tests\isolation')).TrimEnd('\') + '\'
$stage = Join-Path $isolationRoot ('package-stage-' + [Guid]::NewGuid().ToString('N'))
$tempZip = $package + '.tmp'
$tempManifest = $outerManifest + '.tmp'
$publishedZip = $false
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'build') -Destination $stage -Recurse
    New-Item -ItemType Directory -Path (Join-Path $stage 'rules') | Out-Null
    foreach ($name in @('runtime-components.json', 'v3-structure-families.json')) { Copy-Item -LiteralPath (Join-Path $root ('rules\' + $name)) -Destination (Join-Path $stage 'rules') }
    foreach ($relative in @('README.md', 'docs\video-tray-r25.md', 'docs\support-r25.md', 'docs\runtime-retention-r24.md', 'docs\cover-snapshot-r29.md', 'docs\import-video-cover-r30.md')) {
        Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $stage
    }
    if (Test-Path -LiteralPath (Join-Path $stage 'build\data-location.json')) { throw 'Package unexpectedly contains a user-specific binding.' }

    $contract = Get-Content -LiteralPath (Join-Path $stage 'rules\runtime-components.json') -Raw | ConvertFrom-Json
    $externalNames = @($contract.files | Where-Object { $_.name -cne 'Desktop317495Host-refined.exe' } | ForEach-Object { [string]$_.name })
    $files = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        if ($rel -match '(^|/)(data|catalog\.json|settings\.json|logs|sessions|diagnostics|tests)(/|$)' -or $rel -like '*.cwp' -or $rel -match '(?i)cookie|token|credential|secret|diagnostic') { throw "Forbidden package path: $rel" }
        if ($externalNames -contains $_.Name) { throw "Unconfirmed external component must not enter package: $rel" }
        [ordered]@{ path = $rel; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $packageManifest = [ordered]@{
        schemaVersion = 1
        package = 'LocalWallpaper-0.1.30-import-video-cover-internal-r30'
        classification = '不含外部运行组件；第三方本机配合使用条件尚未确认；既有本机CWP观察保留；R28本机402242窗口桌面与托盘用户反馈已收尾；R29手动截图本机通过；R30新增导入前系统离线视频取帧待界面确认；限内部验证'
        version = '0.1.30-import-video-cover-internal-r30'
        entry = 'build/LocalWallpaper.exe'
        dataFormat = @{ min = 1; max = 1 }
        bindingPolicy = '首次使用时由用户选择数据位置；本包不包含用户绑定'
        files = $files
        containsFormalUserData = $false
        containsCwp = $false
        containsDiagnostics = $false
        containsExternalRuntimeBinaries = $false
        externalRuntimeContractCount = 23
        externalRuntimeProvisioning = '用户选择既有安装根；23项完整SHA-256；DLL从原根加载，必要资源归档暂存；不随包分发'
        importCopiesResourceIntoCurrentDataRoot = $true
        importRequiresExplicitUserAction = $true
        importAddsPlaybackConfirmation = $false
        playbackTested = $false
        playerOrDesktopActionDuringBuild = $false
    }
    [IO.File]::WriteAllText((Join-Path $stage 'package-manifest.json'), ($packageManifest | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $tempZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    if ((Get-Item -LiteralPath $tempZip).Length -gt 268435456) { throw 'Package exceeds 256MiB budget.' }
    $zipHash = (Get-FileHash -LiteralPath $tempZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt = [ordered]@{
        schemaVersion = 1
        revision = 'import-video-cover-internal-r30'
        packagePath = ('offline-package/' + $packageName)
        bytes = (Get-Item -LiteralPath $tempZip).Length
        sha256 = $zipHash
        contentCount = $files.Count + 1
        classification = $packageManifest.classification
        createdAt = (Get-Date).ToString('o')
        contentManifest = $packageManifest
        formalUserDataIncluded = $false
    }
    [IO.File]::WriteAllText($tempManifest, ($receipt | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))

    # Verify the temporary archive against the embedded and outer manifests before publishing it.
    $archive = [IO.Compression.ZipFile]::OpenRead($tempZip)
    try {
        $entryMap = @{}
        foreach ($entry in $archive.Entries) { $entryMap[$entry.FullName.Replace('\', '/')] = $entry }
        if ($entryMap.Count -ne $receipt.contentCount -or !$entryMap.ContainsKey('package-manifest.json')) { throw 'Archive member count or embedded manifest mismatch.' }
        $embeddedStream = $entryMap['package-manifest.json'].Open()
        try { $reader = New-Object IO.StreamReader($embeddedStream); $embedded = $reader.ReadToEnd() | ConvertFrom-Json; $reader.Dispose() } finally { $embeddedStream.Dispose() }
        if (@($embedded.files).Count -ne $files.Count) { throw 'Embedded manifest member count mismatch.' }
        foreach ($item in $embedded.files) {
            if (!$entryMap.ContainsKey([string]$item.path)) { throw "Archive member missing: $($item.path)" }
            $entry = $entryMap[[string]$item.path]
            if ($entry.Length -ne [long]$item.bytes) { throw "Archive member size mismatch: $($item.path)" }
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() } finally { $stream.Dispose(); $sha.Dispose() }
            if ($actual -cne [string]$item.sha256) { throw "Archive member hash mismatch: $($item.path)" }
        }
    } finally { $archive.Dispose() }

    Move-Item -LiteralPath $tempZip -Destination $package; $publishedZip = $true
    Move-Item -LiteralPath $tempManifest -Destination $outerManifest
    Write-Output "PACKAGE=$package"
    Write-Output "MANIFEST=$outerManifest"
} catch {
    if (Test-Path -LiteralPath $tempZip) { Remove-Item -LiteralPath $tempZip -Force }
    if (Test-Path -LiteralPath $tempManifest) { Remove-Item -LiteralPath $tempManifest -Force }
    if ($publishedZip -and !(Test-Path -LiteralPath $outerManifest)) { Remove-Item -LiteralPath $package -Force }
    throw
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if (!$resolvedStage.StartsWith($isolationRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean staging outside tests/isolation.' }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}






