#!/usr/bin/env bash
#
# Daniel's Dojo — local SQL Server 2025 Developer workflow (Linux/macOS).
#
#   ./scripts/db.sh start | migrate | seed [profile] | recreate [profile] --confirm | stop | status
#
# Mirrors scripts/db.ps1. Everything is namespaced to Daniel's Dojo and uses a non-default
# host port so it cannot collide with any other local SQL Server. The generated password is
# written outside the repository (.local/, git-ignored) and stored in the API's .NET user
# secrets. No credential or connection string is ever written to a tracked file.

set -Eeuo pipefail

# --- Fixed local target. 'recreate' may never act on anything other than these. ----------
CONTAINER_NAME='danielsdojo-sql'
VOLUME_NAME='danielsdojo-sql-data'
DATABASE_NAME='DanielsDojo'
HOST_PORT=14333
SQL_IMAGE='mcr.microsoft.com/mssql/server:2025-latest'

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCAL_DIR="$ROOT_DIR/.local"
PASSWORD_FILE="$LOCAL_DIR/sql-password.txt"
API_PROJECT="$ROOT_DIR/apps/api/src/DanielsDojo.Api"
INFRA_PROJECT="$ROOT_DIR/apps/api/src/DanielsDojo.Infrastructure"

COMMAND="${1:-status}"
SEED_PROFILE='reference'
CONFIRMED=0

shift || true
while [ $# -gt 0 ]; do
  case "$1" in
    reference|development) SEED_PROFILE="$1" ;;
    --confirm) CONFIRMED=1 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
  shift
done

require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    echo 'ERROR: Docker is required for the local database but was not found on PATH.' >&2
    exit 1
  fi
  if ! docker info >/dev/null 2>&1; then
    echo 'ERROR: Docker is installed but the daemon is not running. Start Docker and retry.' >&2
    exit 1
  fi
}

# Creates the development-only password once and reuses it thereafter.
get_password() {
  if [ -s "$PASSWORD_FILE" ]; then
    # Strips a UTF-8 byte-order mark as well as line endings. Older versions of
    # scripts/db.ps1 wrote the file with a BOM, which PowerShell hides on read but Bash would
    # otherwise send as part of the password, producing a confusing "Login failed" error.
    sed '1s/^\xEF\xBB\xBF//' "$PASSWORD_FILE" | tr -d '\r\n'
    return
  fi

  mkdir -p "$LOCAL_DIR"
  local generated
  generated="Dd1!$(head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9')"
  printf '%s' "$generated" > "$PASSWORD_FILE"
  chmod 600 "$PASSWORD_FILE"
  echo "Generated a new development-only SQL password at $PASSWORD_FILE (git-ignored)." >&2
  printf '%s' "$generated"
}

connection_string() {
  printf 'Server=localhost,%s;Database=%s;User Id=sa;Password=%s;TrustServerCertificate=True;Encrypt=True' \
    "$HOST_PORT" "${2:-$DATABASE_NAME}" "$1"
}

container_state() {
  docker inspect --format '{{.State.Status}}' "$CONTAINER_NAME" 2>/dev/null || echo 'absent'
}

wait_for_sql() {
  local password="$1"
  echo "Waiting for SQL Server on localhost:$HOST_PORT ..."
  for _ in $(seq 1 60); do
    if docker exec "$CONTAINER_NAME" /opt/mssql-tools18/bin/sqlcmd \
         -S localhost -U sa -P "$password" -C -Q 'SELECT 1' >/dev/null 2>&1; then
      echo 'SQL Server is accepting connections.'
      return 0
    fi
    sleep 2
  done
  echo "ERROR: SQL Server did not become ready within 120 seconds. Inspect 'docker logs $CONTAINER_NAME'." >&2
  exit 1
}

set_user_secret() {
  local password="$1"
  dotnet user-secrets set 'ConnectionStrings:DanielsDojoDatabase' \
    "$(connection_string "$password")" --project "$API_PROJECT" >/dev/null
  echo 'Stored the connection string in the API project .NET user secrets.'
}

start_database() {
  require_docker
  local password state
  password="$(get_password)"
  state="$(container_state)"

  case "$state" in
    absent)
      echo "Creating container '$CONTAINER_NAME' on port $HOST_PORT ..."
      docker run -d \
        --name "$CONTAINER_NAME" \
        -e 'ACCEPT_EULA=Y' \
        -e "MSSQL_SA_PASSWORD=$password" \
        -e 'MSSQL_PID=Developer' \
        -p "${HOST_PORT}:1433" \
        -v "${VOLUME_NAME}:/var/opt/mssql" \
        "$SQL_IMAGE" >/dev/null
      ;;
    running) echo "Container '$CONTAINER_NAME' is already running." ;;
    *)
      echo "Starting existing container '$CONTAINER_NAME' ..."
      docker start "$CONTAINER_NAME" >/dev/null
      ;;
  esac

  wait_for_sql "$password"
  set_user_secret "$password"
}

# Runs the explicit database CLI exposed by the API host. Ordinary API startup never
# migrates or seeds; this is the only path that does.
run_database_command() {
  local password="$1"; shift
  ConnectionStrings__DanielsDojoDatabase="$(connection_string "$password")" \
  DANIELSDOJO_DB_CONNECTION="$(connection_string "$password")" \
    dotnet run --project "$API_PROJECT" --no-launch-profile -- "$@"
}

do_migrate() {
  local password; password="$(get_password)"
  echo "Applying migrations to '$DATABASE_NAME' ..."
  run_database_command "$password" database migrate
}

do_seed() {
  local password; password="$(get_password)"
  echo "Seeding '$DATABASE_NAME' with the '$SEED_PROFILE' profile ..."
  if [ "$SEED_PROFILE" = 'development' ]; then
    # ASPNETCORE_ENVIRONMENT gates the development profile inside the seeder itself.
    ASPNETCORE_ENVIRONMENT=Development run_database_command "$password" database seed --profile "$SEED_PROFILE"
  else
    run_database_command "$password" database seed --profile "$SEED_PROFILE"
  fi
}

do_recreate() {
  require_docker

  cat >&2 <<EOF

DESTRUCTIVE OPERATION — the following local target will be deleted and rebuilt:
  container : $CONTAINER_NAME
  volume    : $VOLUME_NAME  (all data in it is lost)
  database  : $DATABASE_NAME on localhost:$HOST_PORT

This command can only ever act on the fixed local target above. It does not accept a
connection string and can never reach a shared or hosted database.

EOF

  if [ "$CONFIRMED" -ne 1 ]; then
    echo "Refusing to recreate without explicit acknowledgement. Rerun with: ./scripts/db.sh recreate --confirm" >&2
    exit 1
  fi

  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
  docker volume rm "$VOLUME_NAME" >/dev/null 2>&1 || true
  echo 'Removed the existing container and volume.'

  start_database
  do_migrate
  do_seed
  echo
  echo "Recreate complete: '$DATABASE_NAME' is migrated and seeded with the '$SEED_PROFILE' profile."
}

do_stop() {
  require_docker
  if [ "$(container_state)" = 'absent' ]; then
    echo "Container '$CONTAINER_NAME' does not exist; nothing to stop."
    return 0
  fi
  docker stop "$CONTAINER_NAME" >/dev/null
  echo "Stopped '$CONTAINER_NAME'. Data is retained in volume '$VOLUME_NAME'."
}

do_status() {
  echo "container : $CONTAINER_NAME [$(container_state)]"
  echo "volume    : $VOLUME_NAME"
  echo "database  : $DATABASE_NAME on localhost:$HOST_PORT"
  echo "image     : $SQL_IMAGE"
  if [ -s "$PASSWORD_FILE" ]; then
    echo "password  : present at $PASSWORD_FILE (git-ignored)"
  else
    echo 'password  : not yet generated — run ./scripts/db.sh start'
  fi

  if [ "$(container_state)" = 'running' ]; then
    local password; password="$(get_password)"
    echo
    echo 'applied migrations:'
    DANIELSDOJO_DB_CONNECTION="$(connection_string "$password")" \
      dotnet ef migrations list --project "$INFRA_PROJECT" --startup-project "$INFRA_PROJECT" 2>/dev/null | tail -n +3
  fi
}

case "$COMMAND" in
  start)    start_database ;;
  migrate)  do_migrate ;;
  seed)     do_seed ;;
  recreate) do_recreate ;;
  stop)     do_stop ;;
  status)   do_status ;;
  *)
    echo "Usage: $0 {start|migrate|seed [profile]|recreate [profile] --confirm|stop|status}" >&2
    exit 1
    ;;
esac
