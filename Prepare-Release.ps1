param(
    [Parameter(Mandatory=$true)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')][string]$Version,
    [Parameter(Mandatory=$true)][string]$ZipPath
)
# Copies a packaged and verified ZIP (Package-Windows.ps1) into Distribution/ with its SHA256 and a
# JSON record (workspace rule: Windows distribution ZIPs live in Distribution/, never overwritten).
# Does not commit or push.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not [IO.Path]::IsPathRooted($ZipPath)) { $ZipPath = Join-Path $PSScriptRoot $ZipPath }
$source = (Resolve-Path -LiteralPath $ZipPath).Path
$rootPrefix = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
if (-not $source.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($source) -ne '.zip') { throw 'Select a local ZIP inside this project.' }
$relative = $source.Substring($rootPrefix.Length).Replace('\', '/')
git -C $PSScriptRoot check-ignore --quiet -- $relative
if ($LASTEXITCODE -ne 0) { throw 'Source ZIP must be Git-ignored package output.' }
if ([IO.Path]::GetFileName($source) -notmatch ('(^|[-_])' + [regex]::Escape($Version) + '([-_.]|$)')) { throw 'ZIP filename must contain the release version.' }
$name = "AF-MilitaryShogi-$Version-Windows.zip"
$destination = Join-Path (Join-Path $PSScriptRoot 'Distribution') $name
foreach ($path in @($destination, "$destination.sha256", "$destination.json")) {
    if (Test-Path -LiteralPath $path) { throw "Release already exists: $path" }
}
$hashPath = "$source.sha256"
if (-not (Test-Path -LiteralPath $hashPath)) { throw 'Package and verify the ZIP (Package-Windows.ps1) first.' }
$expected = ((Get-Content -LiteralPath $hashPath -Raw).Trim() -split '\s+')[0]
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $expected) { throw 'Source ZIP checksum mismatch.' }
if ((Get-Item -LiteralPath $source).Length -ge 100MB) { throw 'ZIP exceeds 100 MB.' }
$archive = [IO.Compression.ZipFile]::OpenRead($source)
try {
    if ($archive.Entries.Count -eq 0) { throw 'Empty archive.' }
    foreach ($entry in $archive.Entries) {
        $entryName = $entry.FullName.Replace('\', '/')
        if ($entryName -match '^/|(^|/)\.\.(/|$)|:|(?i)\.(pfx|key|keystore|jks)$') { throw "Unsafe archive entry: $entryName" }
    }
} finally { $archive.Dispose() }
$commit = git -C $PSScriptRoot rev-parse HEAD
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
$in = [IO.File]::OpenRead($source)
try { $out = [IO.File]::Open($destination, [IO.FileMode]::CreateNew); try { $in.CopyTo($out) } finally { $out.Dispose() } } finally { $in.Dispose() }
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'Copied ZIP checksum mismatch.' }
"$hash  $name" | Set-Content -LiteralPath "$destination.sha256" -Encoding ascii
[ordered]@{
    ReleaseVersion = $Version
    PreparedAt = (Get-Date).ToString('o')
    PreparationCommit = $commit
    SourceZip = $relative
    SHA256 = $hash
    Size = (Get-Item -LiteralPath $destination).Length
    Note = 'Packaged from the verified bin/Release build. Commit and push are separate steps.'
} | ConvertTo-Json | Set-Content -LiteralPath "$destination.json" -Encoding utf8
Write-Host "Prepared $destination"
