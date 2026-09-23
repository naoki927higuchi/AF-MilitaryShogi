[CmdletBinding()]
param([string]$Version)
# Packs the verified Windows Release (bin/Release-<Version>/) into a distribution candidate ZIP:
#   Builds/Package/AF-MilitaryShogi-<Version>-Windows.zip (+ .sha256)
# Contents: the release folder (EXE, UnityPlayer.dll, data, Mono, D3D12, crash handler) and the
# bundled README-ja.txt. No debug artifacts. Must stay below 100 MB. Only for publishing preparation;
# normal builds do not create ZIPs. Hand the ZIP to Prepare-Release.ps1.
$ErrorActionPreference = 'Stop'
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION.txt') -Raw).Trim() }
$release = Join-Path $PSScriptRoot "bin\Release-$Version"
if (-not (Test-Path -LiteralPath (Join-Path $release 'AF-MilitaryShogi.exe'))) { throw "Build the Windows release first: $release" }
$packageDir = Join-Path $PSScriptRoot 'Builds\Package'
New-Item -ItemType Directory -Force $packageDir | Out-Null
$zip = Join-Path $packageDir "AF-MilitaryShogi-$Version-Windows.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
$stage = Join-Path $packageDir "stage-$Version"
if (Test-Path -LiteralPath $stage) { Remove-Item -Recurse -Force -LiteralPath $stage }
$root = Join-Path $stage "AF-MilitaryShogi-$Version"
New-Item -ItemType Directory -Force $root | Out-Null
Get-ChildItem -LiteralPath $release -Force | Where-Object { $_.Name -notmatch '(?i)DoNotShip|\.pdb$' } | Copy-Item -Destination $root -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Distribution\README-ja.txt') -Destination (Join-Path $root 'README-ja.txt')
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -Recurse -Force -LiteralPath $stage
$size = (Get-Item -LiteralPath $zip).Length
if ($size -ge 100MB) { throw "ZIP is $([math]::Round($size / 1MB, 1)) MB (limit 100 MB)." }
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = $archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
    foreach ($required in @("AF-MilitaryShogi-$Version/AF-MilitaryShogi.exe", "AF-MilitaryShogi-$Version/UnityPlayer.dll", "AF-MilitaryShogi-$Version/README-ja.txt")) {
        if ($names -notcontains $required) { throw "Missing in ZIP: $required" }
    }
    $bad = @($names | Where-Object { $_ -match '(?i)DoNotShip|\.pdb$|\.mdb$|/userdata/|/remote/|\.keystore$|\.dpapi$' })
    if ($bad.Count) { throw "Unwanted entries: $($bad -join ', ')" }
    foreach ($entry in $archive.Entries) { $s = $entry.Open(); try { $s.CopyTo([IO.Stream]::Null) } finally { $s.Dispose() } }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
Write-Host ("PASS: {0} ({1:N1} MB, {2} entries)" -f $zip, ($size / 1MB), $names.Count)
Write-Output $zip
