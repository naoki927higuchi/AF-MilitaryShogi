[CmdletBinding()]
param([switch]$Quick)
# Rule / engine / CPU / information-boundary tests (pure C#, no Unity needed).
$ErrorActionPreference = 'Stop'
$runArgs = @()
if ($Quick) { $runArgs += '--quick' }
dotnet run --project (Join-Path $PSScriptRoot 'Tests\Runner\Runner.csproj') -c Release -- @runArgs
if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)" }
