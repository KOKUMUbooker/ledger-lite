#!/usr/bin/env bash
# Local SQL Server in Docker, for running LedgerLite on a Linux machine.
# The SA password lives in .env as MSSQL_SA_PASSWORD (generated on first run).
#
# Usage:
#   scripts/db-local.sh up [--write-env]   start; --write-env sets ConnectionStrings__LedgerDb in .env
#   scripts/db-local.sh status             container state
#   scripts/db-local.sh conn               print the connection strings
#   scripts/db-local.sh stop               stop, keep the data
#   scripts/db-local.sh destroy            stop and DELETE the data volume
set -euo pipefail
cd "$(dirname "$0")/.."

# Compose reads .env in this directory by itself for ${MSSQL_SA_PASSWORD} etc.
COMPOSE=(docker compose -f docker-compose.db.yml)
DB_NAME=LedgerLite

command -v docker >/dev/null || { echo "Docker is not installed." >&2; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "Docker Compose v2 is required." >&2; exit 1; }

# Read one variable from .env without executing the file (it holds secrets and semicolons)
env_get() { grep -m1 "^$1=" .env 2>/dev/null | cut -d= -f2- || true; }

ensure_secrets() {
  [ -f .env ] || cp .env.example .env
  if [ -z "$(env_get MSSQL_SA_PASSWORD)" ]; then
    # Alphanumeric plus a fixed prefix: meets SQL Server's complexity rules, needs no escaping
    printf '\nMSSQL_SA_PASSWORD=Ll1-%s\n' "$(openssl rand -hex 12)" >> .env
    echo "Added a generated MSSQL_SA_PASSWORD to .env"
  fi
  MSSQL_SA_PASSWORD=$(env_get MSSQL_SA_PASSWORD)
  MSSQL_PORT=$(env_get MSSQL_PORT)
  MSSQL_PORT=${MSSQL_PORT:-1433}
  export MSSQL_SA_PASSWORD MSSQL_PORT
}

conn_string() {
  echo "Server=localhost,${MSSQL_PORT};Database=${DB_NAME};User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;MultipleActiveResultSets=True"
}

test_conn_string() {   # the tests create and drop their own uniquely named databases
  echo "Server=localhost,${MSSQL_PORT};User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True"
}

print_conn() {
  echo
  echo "App (.env):"
  echo "  ConnectionStrings__LedgerDb=$(conn_string)"
  echo
  echo "Tests:"
  echo "  export LEDGERLITE_TEST_CONNECTION=\"$(test_conn_string)\""
}

write_env() {
  local line; line="ConnectionStrings__LedgerDb=$(conn_string)"
  if grep -q '^ConnectionStrings__LedgerDb=' .env; then
    # '|' as the sed delimiter: the generated password never contains it
    sed -i "s|^ConnectionStrings__LedgerDb=.*|${line}|" .env
  else
    printf '%s\n' "$line" >> .env
  fi
  echo "Updated ConnectionStrings__LedgerDb in .env"
}

case "${1:-}" in
  up)
    ensure_secrets
    "${COMPOSE[@]}" up -d
    echo -n "Waiting for SQL Server to accept connections"
    state=starting
    for _ in $(seq 1 60); do
      state=$(docker inspect -f '{{.State.Health.Status}}' ledgerlite-sqlserver 2>/dev/null || echo starting)
      [ "$state" = healthy ] && break
      echo -n "."; sleep 2
    done
    echo
    if [ "$state" != healthy ]; then
      echo "SQL Server did not become healthy. Check: docker logs ledgerlite-sqlserver" >&2
      echo "(It needs about 2 GB of free RAM.)" >&2
      exit 1
    fi
    echo "SQL Server is ready on 127.0.0.1:${MSSQL_PORT}"
    if [ "${2:-}" = "--write-env" ]; then write_env; fi
    print_conn
    echo
    echo "Next:  dotnet ef database update -p LedgerLite.Data -s LedgerLite.Api"
    echo "  or:  scripts/reset-db.sh --seed"
    ;;
  status)  docker ps -a --filter name=ledgerlite-sqlserver --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' ;;
  conn)    ensure_secrets; print_conn ;;
  stop)    ensure_secrets; "${COMPOSE[@]}" stop ;;
  destroy)
    ensure_secrets
    read -r -p "This DELETES the local database volume. Type DESTROY to continue: " answer
    [ "$answer" = "DESTROY" ] || { echo "Cancelled."; exit 1; }
    "${COMPOSE[@]}" down -v
    ;;
  *) echo "Usage: $0 up [--write-env] | status | conn | stop | destroy" >&2; exit 1 ;;
esac