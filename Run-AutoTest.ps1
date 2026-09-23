[CmdletBinding()]
param([string]$Exe, [string]$Seeds = '1001,2002,3003')
# Plays full games in the built player and verifies them (see AutoPilot.cs):
#  run A: normal run with screenshots of 対戦/研究 modes and 「あそびかた」
#  run B: same seeds, presentation mode flipped after every ply (-toggleModes)
# The final session fingerprints of A and B must be identical. Also checks the EXE's icon.
# Output: Builds/AutoTest/<timestamp>/{A,B}/
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION.txt') -Raw).Trim()
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot "bin\Release-$version\AF-MilitaryShogi.exe" }
$root = Join-Path $PSScriptRoot ('Builds\AutoTest\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
$failed = $false
$prints = @{}
foreach ($run in @(@{ Name = 'A'; Extra = @() }, @{ Name = 'B'; Extra = @('-toggleModes') })) {
    $out = Join-Path $root $run.Name
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $p = Start-Process -FilePath $Exe -ArgumentList (@('-autotest', $out, '-seeds', $Seeds, '-logFile', (Join-Path $out 'player.log')) + $run.Extra) -PassThru -Wait
    $report = Join-Path $out 'autotest_report.txt'
    Get-Content -LiteralPath $report -TotalCount 10 -Encoding UTF8
    $prints[$run.Name] = (Select-String -LiteralPath $report -Pattern '^FINAL_FINGERPRINT=(.*)$').Matches[0].Groups[1].Value
    if ($p.ExitCode -ne 0) { $failed = $true; Write-Host "Run $($run.Name) failed (exit $($p.ExitCode))" }
}
if ($prints['A'] -ne $prints['B']) { $failed = $true; Write-Host "Mode toggling changed the game: $($prints['A']) vs $($prints['B'])" }
else { Write-Host "Fingerprint with and without mode toggling: $($prints['A']) (identical)" }

# Application icon: the EXE's icon must be the generated one, not Unity's default.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon($Exe).ToBitmap()
$icon.Save((Join-Path $root 'exe_icon.png'))
$ref = [System.Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot 'Assets\Generated\Icons\app_icon_32.png'))
$diff = 0.0; $n = 0
for ($y = 0; $y -lt 32; $y += 2) { for ($x = 0; $x -lt 32; $x += 2) {
    $a = $icon.GetPixel($x, $y); $b = $ref.GetPixel($x, $y)
    if ($b.A -gt 128) { $diff += ([math]::Abs($a.R - $b.R) + [math]::Abs($a.G - $b.G) + [math]::Abs($a.B - $b.B)) / 3.0; $n++ }
} }
$ref.Dispose()
$mean = $diff / [math]::Max(1, $n)
Write-Host ("EXE icon vs generated app icon: mean difference {0:N1} / 255" -f $mean)
if ($mean -gt 40) { $failed = $true; Write-Host 'EXE icon does not match the generated application icon.' }
Write-Output "Output: $root"
if ($failed) { throw 'Autotest failed' }
