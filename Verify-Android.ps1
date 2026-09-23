[CmdletBinding()]
param([Parameter(Mandatory)][string]$Apk, [Parameter(Mandatory)][string]$AndroidRoot)
# Checks a Release APK: package and version, ARM64 IL2CPP, not debuggable, signed with the project's
# release key (not the Android debug key), no managed/native debug artifacts. Writes SHA256, manifest
# and signature reports next to the APK.
$ErrorActionPreference = 'Stop'
$buildTools = Get-ChildItem (Join-Path $AndroidRoot 'SDK\build-tools') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$env:JAVA_HOME = Join-Path $AndroidRoot 'OpenJDK'
$badging = & (Join-Path $buildTools.FullName 'aapt.exe') dump badging $Apk
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect APK manifest.' }
if ($badging -match 'application-debuggable') { throw 'APK is debuggable.' }
if (-not ($badging -match "native-code: 'arm64-v8a'")) { throw 'APK must contain ARM64 code.' }
if (-not ($badging -match "package: name='com.af.militaryshogi'")) { throw 'Unexpected package identity.' }
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION.txt') -Raw).Trim()
if (-not ($badging -match "versionName='$([regex]::Escape($version))'")) { throw 'APK version differs from VERSION.txt.' }
$signature = & (Join-Path $buildTools.FullName 'apksigner.bat') verify --verbose --print-certs $Apk 2>&1
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
if ($signature -match 'CN=Android Debug') { throw 'APK uses the Android debug signing key.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Apk))
try {
    $unwanted = @($zip.Entries | Where-Object { $_.FullName -match '(?i)(\.pdb$|\.mdb$|\.sym$|\.dbg$|symbols\.zip|DoNotShip|ButDontShipIt|libmono|libdebugger)' })
    if ($unwanted.Count) { throw "Debug artifacts found: $($unwanted.FullName -join ', ')" }
    if (-not ($zip.Entries.FullName -contains 'lib/arm64-v8a/libil2cpp.so')) { throw 'IL2CPP runtime missing.' }
    $readelf = Join-Path $AndroidRoot 'NDK\toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
    if (-not (Test-Path -LiteralPath $readelf)) { throw 'NDK llvm-readelf is required to verify native symbol stripping.' }
    $inspect = Join-Path $PSScriptRoot ('Builds\Verification\' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $inspect | Out-Null
    foreach ($entry in @($zip.Entries | Where-Object FullName -match '^lib/.*\.so$')) {
        $library = Join-Path $inspect ([IO.Path]::GetFileName($entry.FullName))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $library)
        $sections = & $readelf --sections $library
        if ($LASTEXITCODE -ne 0) { throw "Cannot inspect native library: $($entry.FullName)" }
        if ($sections -match '\.debug_|\.zdebug_|\.symtab') { throw "Unstripped native debug symbols: $($entry.FullName)" }
    }
} finally { $zip.Dispose() }
$hash = (Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($Apk))" | Set-Content -LiteralPath "$Apk.sha256" -Encoding ascii
$badging | Set-Content -LiteralPath "$Apk.manifest.txt"
$signature | Set-Content -LiteralPath "$Apk.signature.txt"
Write-Host 'PASS: signed ARM64 IL2CPP APK, matching version, not debuggable, no debug artifacts or native debug symbol sections.'
