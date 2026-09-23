[CmdletBinding()]
param([string]$Exe, [string]$Seeds = '1001,2002,3003')
# Plays one full game in the built player, checks that enemy faces are never rendered,
# and writes screenshots + autotest_report.txt to Builds/AutoTest/<timestamp>/.
$ErrorActionPreference = 'Stop'
if (-not $Exe) {
    $version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION.txt') -Raw).Trim()
    $Exe = Join-Path $PSScriptRoot "bin\Release-$version\AF-MilitaryShogi.exe"
}
$out = Join-Path $PSScriptRoot ('Builds\AutoTest\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $out | Out-Null
$p = Start-Process -FilePath $Exe -ArgumentList @('-autotest', $out, '-seeds', $Seeds, '-logFile', (Join-Path $out 'player.log')) -PassThru -Wait
Get-Content -LiteralPath (Join-Path $out 'autotest_report.txt') -TotalCount 9
Write-Output "Output: $out"
if ($p.ExitCode -ne 0) { throw "Autotest failed (exit $($p.ExitCode))" }
