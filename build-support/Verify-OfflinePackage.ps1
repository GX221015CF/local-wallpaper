param(
    [string]$PackagePath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'offline-package\LocalWallpaper-0.1.31-personal-experience-internal-r31.zip'),
    [string]$ManifestPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'offline-package\manifest-personal-experience-internal-r31.json'),
    [string]$EvidencePath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = [IO.Path]::GetFullPath($PackagePath)
$manifestFile = [IO.Path]::GetFullPath($ManifestPath)
$receipt = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
    if ($receipt.schemaVersion -ne 1 -or $receipt.revision -ne 'personal-experience-internal-r31') { throw 'Unsupported outer manifest.' }
if ([IO.Path]::GetFileName($package) -cne 'LocalWallpaper-0.1.31-personal-experience-internal-r31.zip') { throw 'Unexpected package name.' }
$info = Get-Item -LiteralPath $package
$outerHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($info.Length -ne [long]$receipt.bytes -or $outerHash -cne [string]$receipt.sha256) { throw 'Outer package size/hash mismatch.' }
$zip = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $entries = @{}
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if ($name.StartsWith('/') -or $name -match '(^|/)\.\.?(/|$)' -or $name.Contains(':') -or $entries.ContainsKey($name)) { throw "Unsafe or duplicate ZIP path: $name" }
        $entries[$name] = $entry
    }
    if ($entries.Count -ne [int]$receipt.contentCount -or !$entries.ContainsKey('package-manifest.json')) { throw 'ZIP member count or package manifest mismatch.' }
    $stream = $entries['package-manifest.json'].Open()
    try { $reader = New-Object IO.StreamReader($stream); $inner = $reader.ReadToEnd() | ConvertFrom-Json; $reader.Dispose() } finally { $stream.Dispose() }
    if ($inner.package -cne 'LocalWallpaper-0.1.31-personal-experience-internal-r31' -or $inner.entry -cne 'build/LocalWallpaper.exe') { throw 'Embedded package identity or entry mismatch.' }
    if ($inner.containsFormalUserData -or $inner.containsCwp -or $inner.containsDiagnostics -or $receipt.formalUserDataIncluded) { throw 'Forbidden user data flag in package.' }
    $expected = @{}
    foreach ($file in $inner.files) {
        $name = [string]$file.path
        if (!$name -or $name.StartsWith('/') -or $name -match '(^|/)\.\.?(/|$)' -or $name.Contains(':') -or $expected.ContainsKey($name)) { throw "Unsafe or duplicate manifest path: $name" }
        $expected[$name] = $file
        if (!$entries.ContainsKey($name)) { throw "Missing package member: $name" }
        $entry = $entries[$name]
        if ($entry.Length -ne [long]$file.bytes) { throw "Package member size mismatch: $name" }
        $content = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($content)).Replace('-', '').ToLowerInvariant() } finally { $content.Dispose(); $sha.Dispose() }
        if ($hash -cne [string]$file.sha256) { throw "Package member hash mismatch: $name" }
    }
    foreach ($name in $entries.Keys) { if ($name -cne 'package-manifest.json' -and !$expected.ContainsKey($name)) { throw "Unlisted ZIP member: $name" } }
    foreach ($required in @('build/LocalWallpaper.exe','build/LocalWallpaperLauncher.exe','build/WallpaperLocalTool.exe','build/WallpaperVersionTool.exe','build/runtime-manifest.json','rules/runtime-components.json','rules/v3-structure-families.json','README.md','video-tray-r25.md','support-r25.md','runtime-retention-r24.md','cover-snapshot-r29.md','import-video-cover-r30.md','personal-experience-r31.md')) {
        if (!$entries.ContainsKey($required)) { throw "Required package file missing: $required" }
    }
    foreach ($forbidden in @('build/data-location.json')) { if ($entries.ContainsKey($forbidden)) { throw "Forbidden per-user binding present: $forbidden" } }
    if ($entries.Keys | Where-Object { $_ -match '(?i)(^|/)(catalog\.json|settings\.json|logs|sessions|diagnostics|tests)(/|$)|\.cwp$|cookie|token|credential|secret' }) { throw 'Package contains a forbidden user-data or diagnostic path.' }
    if ($inner.containsExternalRuntimeBinaries -ne $false -or $inner.externalRuntimeContractCount -ne 23 -or $inner.playbackTested -ne $false -or $inner.importCopiesResourceIntoCurrentDataRoot -ne $true -or $inner.importRequiresExplicitUserAction -ne $true -or $inner.importAddsPlaybackConfirmation -ne $false) { throw 'Package scope or import behavior flags are missing or inconsistent.' }
    $contractStream=$entries['rules/runtime-components.json'].Open()
    try{$contractReader=New-Object IO.StreamReader($contractStream);$contract=$contractReader.ReadToEnd()|ConvertFrom-Json;$contractReader.Dispose()}finally{$contractStream.Dispose()}
    $externalNames=@($contract.files|Where-Object{$_.name -cne 'Desktop317495Host-refined.exe'}|ForEach-Object{[string]$_.name})
    foreach($name in $entries.Keys){if($externalNames -contains [IO.Path]::GetFileName($name)){throw "Unconfirmed external binary included: $name"}}
    $report = [ordered]@{
        schemaVersion = 1
        verifiedAt = (Get-Date).ToString('o')
        packagePath = $package
        packageBytes = $info.Length
        packageSha256 = $outerHash
        outerManifestMatched = $true
        memberCount = $entries.Count
        listedFileCount = @($inner.files).Count
        allMemberSizesMatched = $true
        allMemberSha256Matched = $true
        requiredFilesPresent = $true
        containsFormalUserData = $false
        containsCwp = $false
        containsDiagnostics = $false
        containsExternalRuntimeBinaries = $false
        containsUserBinding = $false
        classification = [string]$inner.classification
        playbackTested = $false
        entry = [string]$inner.entry
        result = 'passed'
    }
} finally { $zip.Dispose() }
$json = $report | ConvertTo-Json -Depth 5
if ($EvidencePath) { $evidence=[IO.Path]::GetFullPath($EvidencePath);New-Item -ItemType Directory -Path (Split-Path -Parent $evidence) -Force|Out-Null;[IO.File]::WriteAllText($evidence, $json, (New-Object Text.UTF8Encoding($false))) }
Write-Output $json
