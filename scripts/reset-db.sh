#!/usr/bin/env bash
# Deletes every table in the configured database, rebuilds it (migrations + starter accounts),
# and optionally loads demo data.
#
# Usage:
#   ./scripts/reset-db.sh          # reset only
#   ./scripts/reset-db.sh --seed   # reset, then load demo data
set -euo pipefail
cd "$(dirname "$0")/.."   # repo root, so .env is found

seed=false
case "${1:-}" in
  "")     ;;
  --seed) seed=true ;;
  *)      echo "Usage: $0 [--seed]" >&2; exit 1 ;;
esac

# Step 1: show which database this would hit. Without --yes the app only prints the target and exits with 2.
# "|| code=$?" stops set -e from aborting on the expected non-zero exit.
code=0
dotnet run --project LedgerLite.Api -- reset-db || code=$?
if [ "$code" -ne 2 ]; then
  echo "Could not check the target database (exit code $code)." >&2
  exit 1
fi

read -r -p "This will DELETE ALL DATA in the database above. Type RESET to continue: " answer
if [ "$answer" != "RESET" ]; then
  echo "Cancelled."
  exit 1
fi

# Step 2: do it
args=(reset-db --yes)
if [ "$seed" = true ]; then args+=(seed-demo); fi
dotnet run --project LedgerLite.Api -- "${args[@]}"