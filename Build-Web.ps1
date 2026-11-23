[CmdletBinding()]
param([string]$UnityEditor, [switch]$OverwriteUnpublished)
# Builds the Web (WebGL) version into bin/Web-<VERSION>/ (index.html, Build/) with Unity in batch mode.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$editorVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ProjectSettings\ProjectVersion.txt') -Raw), '(?m)^m_EditorVersion:\s*(\S+)').Groups[1].Value
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe" }
if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw "Unity $editorVersion was not found. Pass -UnityEditor." }
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION.txt') -Raw).Trim()
$lockPath = Join-Path $PSScriptRoot 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    try { $probe = [IO.File]::Open($lockPath, 'Open', 'Read', 'None'); $probe.Dispose() }
    catch { throw 'This project is open in Unity. Use MilitaryShogi > Build Web, or close the Editor.' }
}
$logDir = Join-Path $PSScriptRoot 'Builds\Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-build-web.log')
$extra = @()
if ($OverwriteUnpublished) { $extra += '-overwriteUnpublished' }
Write-Host "Building AF-MilitaryShogi $version for Web with Unity $editorVersion (log: $log)"
& $UnityEditor -batchmode -quit -nographics -projectPath $PSScriptRoot -buildTarget WebGL -executeMethod MilitaryShogi.Editor.WebBuild.BuildWeb @extra -logFile $log | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Unity build failed (exit $LASTEXITCODE). See $log" }
$line = Get-Content -LiteralPath $log | Where-Object { $_ -match '^WEB_BUILD=' } | Select-Object -Last 1
if (-not $line) { throw "Unity did not report a Web build. See $log" }
$out = $line.Substring('WEB_BUILD='.Length).Trim()
foreach ($f in 'index.html', 'Build') { if (-not (Test-Path -LiteralPath (Join-Path $out $f))) { throw "Missing in the Web build: $f" } }
Write-Output $out
