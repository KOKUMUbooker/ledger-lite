<# Loads demo data into an EMPTY database. Use reset-db.ps1 first if it already has data. #>
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet run --project LedgerLite.Api -- seed-demo
if ($LASTEXITCODE -ne 0) { throw "Seeding failed (exit code $LASTEXITCODE)." }