#!/usr/bin/env bash
# Local SQL Server in Docker, for running LedgerLite on a Linux machine.
#
# Usage:
#   scripts/db-local.sh up [--write-env]   start (creates the password on first run); --write-env updates .env
#   scripts/db-local.sh status             container state
#   scripts/db-local.sh conn               print the connection strings
#   scripts/db-local.sh stop               stop, keep the data
#   scripts/db-local.sh destroy            stop and DELETE the data volume
set -euo pipefail
cd "$(dirname "$0")/.."

COMPOSE=(docker compose -f docker-compose.db.yml --env-file .env.db)
SECRETS=.env.db        # holds the generated SA password; gitignored
DB_NAME=LedgerLite

command -v docker >/dev/null || { echo "Docker is not installed." >&2; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "Docker Compose v2 is required." >&2; exit 1; }

ensure_secrets() {
  if [ ! -f "$SECRETS" ]; then
    # Alphanumeric plus a fixed suffix: meets SQL Server's complexity rules and needs no escaping
    local pw; pw="Ll1-$(openssl rand -hex 12)"
    umask 077
    printf 'MSSQL_SA_PASSWORD=%s\nMSSQL_PORT=1433\n' "$pw" > "$SECRETS"
    echo "Created $SECRETS with a generated SA password."
  fi
  set -a; . "./$SECRETS"; set +a
}

conn_string() {
  echo "Server=localhost,${MSSQL_PORT:-1433};Database=${DB_NAME};User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;MultipleActiveResultSets=True"
}

test_conn_string() {   # the tests create and drop their own uniquely named databases
  echo "Server=localhost,${MSSQL_PORT:-1433};User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True"
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
  [ -f .env ] || cp .env.example .env
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
    echo "SQL Server is ready on 127.0.0.1:${MSSQL_PORT:-1433}"
    [ "${2:-}" = "--write-env" ] && write_env
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