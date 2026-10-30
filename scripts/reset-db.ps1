<#
.SYNOPSIS
  Deletes every table in the configured database, rebuilds it (migrations + starter accounts),
  and optionally loads demo data.
.EXAMPLE
  ./scripts/reset-db.ps1          # reset only
  ./scripts/reset-db.ps1 -Seed    # reset, then load demo data
#>
param([switch]$Seed)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')   # repo root, so .env is found

# Step 1: show which database this would hit. Without --yes the app only prints the target and stops (exit 2).
dotnet run --project LedgerLite.Api -- reset-db
if ($LASTEXITCODE -ne 2) { throw "Could not check the target database (exit code $LASTEXITCODE)." }

$answer = Read-Host "This will DELETE ALL DATA in the database above. Type RESET to continue"
if ($answer -ne 'RESET') { Write-Host 'Cancelled.'; exit 1 }

# Step 2: do it
$cmd = @('reset-db', '--yes')
if ($Seed) { $cmd += 'seed-demo' }
dotnet run --project LedgerLite.Api -- @cmd
if ($LASTEXITCODE -ne 0) { throw "Reset failed (exit code $LASTEXITCODE)." }