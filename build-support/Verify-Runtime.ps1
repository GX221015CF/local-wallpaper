param([string]$Root=(Split-Path $PSScriptRoot -Parent),[switch]$Generate)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$contract=Join-Path $Root 'rules\runtime-components.json'
$contractHash=(Get-FileHash -LiteralPath $contract -Algorithm SHA256).Hash.ToLowerInvariant()
if($contractHash -cne '3e773a7cde1ef773ad0730901251fe390462b22f4bf35a927acd2e8675f08b96'){throw 'Runtime contract identity mismatch'}
$expected=Get-Content -LiteralPath $contract -Raw|ConvertFrom-Json
if(@($expected.files).Count -ne 24){throw 'Expected exactly24 contract rows (23 external + 1 self-owned host)'}
$hostRow=@($expected.files|Where-Object{$_.name -ceq 'Desktop317495Host-refined.exe'})
if($hostRow.Count -ne 1 -or $hostRow[0].sha256 -notmatch '^[a-f0-9]{64}$'){throw 'Self-owned host identity row missing/invalid'}
$hostPath=Join-Path $Root 'build\runtime\Desktop317495Host-refined.exe'
if(!(Test-Path -LiteralPath $hostPath -PathType Leaf)){throw 'Self-owned host build is missing'}
if((Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hostRow[0].sha256){throw 'Self-owned host identity mismatch'}
$manifest=Join-Path $Root 'build\runtime-manifest.json'
$verified=@([ordered]@{name='Desktop317495Host-refined.exe';sha256=$hostRow[0].sha256})
if($Generate -and !(Test-Path -LiteralPath $manifest)){
 $tmp=$manifest+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
 try{[IO.File]::WriteAllText($tmp,(@{files=$verified}|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)));[IO.File]::Move($tmp,$manifest)}finally{if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp}}
}
$actual=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
if(@($actual.files).Count -ne 1 -or $actual.files[0].name -cne 'Desktop317495Host-refined.exe' -or $actual.files[0].sha256 -cne $hostRow[0].sha256){throw 'Self-owned runtime manifest mismatch'}
$rules=Join-Path $Root 'rules\v3-structure-families.json'
if(!(Test-Path -LiteralPath $rules -PathType Leaf)){throw 'Required structure rules missing'}
$null=Get-Content -LiteralPath $rules -Raw|ConvertFrom-Json
Write-Output 'PASS: program-owned host and rules verified; 23 external components are not bundled and are checked only from the selected local root.'
