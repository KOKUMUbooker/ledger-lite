#!/usr/bin/env bash
# Loads demo data into an EMPTY database. Use reset-db.sh first if it already has data.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet run --project LedgerLite.Api -- seed-demo