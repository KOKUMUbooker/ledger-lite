#!/usr/bin/env bash
# Usage: scripts/db.sh reset | seed | fresh      (fresh = reset, then load demo data)
set -euo pipefail
cd "$(dirname "$0")/.."

run() { docker compose run --rm api "$@"; }

case "${1:-}" in
  seed)
    run seed-demo
    ;;
  reset|fresh)
    run reset-db --production || true          # no --yes: just prints the target, exits 2
    read -r -p "This will DELETE ALL DATA in the database above. Type RESET to continue: " answer
    [ "$answer" = "RESET" ] || { echo "Cancelled."; exit 1; }
    if [ "$1" = "fresh" ]; then run reset-db --yes --production seed-demo; else run reset-db --yes --production; fi
    ;;
  *)
    echo "Usage: $0 reset | seed | fresh"; exit 1
    ;;
esac