[CmdletBinding()]
param([string]$UnityEditor, [switch]$OverwriteUnpublished)
# Builds the Android Release APK into bin/Android/Release-<VERSION>/AF-MilitaryShogi-<VERSION>.apk
# (ARM64 / IL2CPP Release, signed with this project's local release key) and verifies it.
# The key and its DPAPI-encrypted password live in .local/android-signing/ (not in Git).
# The APK is for installing on your own device; it is not distributed.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$editorVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ProjectSettings\ProjectVersion.txt') -Raw), '(?m)^m_EditorVersion:\s*(\S+)').Groups[1].Value
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe" }
$android = Join-Path (Split-Path $UnityEditor) 'Data\PlaybackEngines\AndroidPlayer'
$keytool = Join-Path $android 'OpenJDK\bin\keytool.exe'
if (-not (Test-Path -LiteralPath $keytool)) { throw 'Install Android Build Support with SDK, NDK and OpenJDK in Unity Hub.' }
$lockPath = Join-Path $PSScriptRoot 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    try { $probe = [IO.File]::Open($lockPath, 'Open', 'Read', 'None'); $probe.Dispose() }
    catch { throw 'This project is open in Unity. Close the Editor first.' }
}
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION.txt') -Raw).Trim()
$output = Join-Path $PSScriptRoot "bin\Android\Release-$version\AF-MilitaryShogi-$version.apk"
if ((Test-Path -LiteralPath $output) -and -not $OverwriteUnpublished) { throw "Release APK already exists: $output" }
$local = Join-Path $PSScriptRoot '.local\android-signing'
New-Item -ItemType Directory -Force $local | Out-Null
$secretPath = Join-Path $local 'password.dpapi'
$keyPath = Join-Path $local 'militaryshogi.keystore'
if (-not (Test-Path -LiteralPath $secretPath)) {
    if (Test-Path -LiteralPath $keyPath) { throw 'Existing key has no password file. Restore the signing credentials.' }
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $secret = ConvertTo-SecureString ([Convert]::ToBase64String($bytes)) -AsPlainText -Force
    $secret | ConvertFrom-SecureString | Set-Content -LiteralPath $secretPath
}
$secure = (Get-Content -LiteralPath $secretPath -Raw).Trim() | ConvertTo-SecureString
$env:AFMS_KEY_PASSWORD = [Net.NetworkCredential]::new('', $secure).Password
$env:AFMS_KEYSTORE = $keyPath
try {
    if (-not (Test-Path -LiteralPath $keyPath)) {
        & $keytool -genkeypair -keystore $keyPath -storetype JKS -alias militaryshogi -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=AF-MilitaryShogi, O=AF' -storepass:env AFMS_KEY_PASSWORD -keypass:env AFMS_KEY_PASSWORD
        if ($LASTEXITCODE -ne 0) { throw 'Release signing key generation failed.' }
    }
    $logDir = Join-Path $PSScriptRoot 'Builds\Logs'
    New-Item -ItemType Directory -Force $logDir | Out-Null
    $log = Join-Path $logDir ('android-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '.log')
    $extra = @()
    if ($OverwriteUnpublished) { $extra += '-overwriteUnpublished' }
    Write-Host "Building AF-MilitaryShogi $version APK with Unity $editorVersion (log: $log)"
    & $UnityEditor -batchmode -quit -nographics -projectPath $PSScriptRoot -buildTarget Android -executeMethod MilitaryShogi.Editor.AndroidBuild.BuildRelease @extra -logFile $log | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw "Android build failed. See $log" }
    & (Join-Path $PSScriptRoot 'Verify-Android.ps1') -Apk $output -AndroidRoot $android
    Write-Output $output
} finally {
    Remove-Item Env:AFMS_KEY_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:AFMS_KEYSTORE -ErrorAction SilentlyContinue
}
