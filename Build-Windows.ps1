[CmdletBinding()]
param([string]$UnityEditor, [switch]$OverwriteUnpublished)
# Builds the Windows Release into bin/Release-<VERSION>/AF-MilitaryShogi.exe (Unity batch mode).
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$editorVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ProjectSettings\ProjectVersion.txt') -Raw), '(?m)^m_EditorVersion:\s*(\S+)').Groups[1].Value
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe" }
if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw "Unity $editorVersion was not found. Pass -UnityEditor." }
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION.txt') -Raw).Trim()
$lockPath = Join-Path $PSScriptRoot 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    try { $probe = [IO.File]::Open($lockPath, 'Open', 'Read', 'None'); $probe.Dispose() }
    catch { throw 'This project is open in Unity. Use MilitaryShogi > Build Release, or close the Editor.' }
}
$logDir = Join-Path $PSScriptRoot 'Builds\Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-build.log')
$extra = @()
if ($OverwriteUnpublished) { $extra += '-overwriteUnpublished' }
Write-Host "Building AF-MilitaryShogi $version with Unity $editorVersion (log: $log)"
& $UnityEditor -batchmode -quit -nographics -projectPath $PSScriptRoot -buildTarget Win64 -executeMethod MilitaryShogi.Editor.WindowsBuild.BuildRelease -releaseVersion $version @extra -logFile $log | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Unity build failed (exit $LASTEXITCODE). See $log" }
$iconLine = Get-Content -LiteralPath $log | Where-Object { $_ -match '^APP_ICON_SET=' } | Select-Object -Last 1
if (-not $iconLine -or $iconLine -match 'null') { throw "Application icon was not set. See $log" }
Write-Host $iconLine
$line = Get-Content -LiteralPath $log | Where-Object { $_ -match '^RELEASE_EXE=' } | Select-Object -Last 1
if (-not $line) { throw "Unity did not report a release EXE. See $log" }
$exe = $line.Substring('RELEASE_EXE='.Length).Trim()
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Reported EXE does not exist.' }
Write-Output $exe
