#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
ENV_FILE="${BIBLIOTECA_ENV_FILE:-.env}"
if [ ! -f "$ENV_FILE" ]; then ENV_FILE="$HOME/Documents/Codex/.secrets/biblioteca-virtual.env"; fi
if [ ! -f "$ENV_FILE" ]; then echo 'Configura BIBLIOTECA_ENV_FILE con la ruta de tu archivo de conexión.'; exit 1; fi
set -a
source "$ENV_FILE"
set +a
DOTNET="${DOTNET_BIN:-dotnet}"
if ! command -v "$DOTNET" >/dev/null && [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"; fi
export ConnectionStrings__Library="Server=$DB_HOST;Port=${DB_PORT:-3306};Database=${DB_NAME:-biblioteca_virtual_p3};User=$DB_USER;Password=$DB_PASSWORD;SslMode=Preferred;"
export ASPNETCORE_ENVIRONMENT=Development
export Demo__EnableFailureSimulation="${Demo__EnableFailureSimulation:-false}"

"$DOTNET" build BibliotecaVirtual.slnx
"$DOTNET" run --no-build --project src/Biblioteca.Api -- --migrate
"$DOTNET" run --no-build --project src/Biblioteca.Api -- --create-admin
"$DOTNET" run --no-build --project src/Biblioteca.Api -- --seed

"$DOTNET" run --no-build --project src/Biblioteca.Api --urls http://localhost:5081 &
api_pid=$!
"$DOTNET" run --no-build --project src/Biblioteca.Web --urls http://localhost:5080 &
web_pid=$!
trap 'kill "$api_pid" "$web_pid" 2>/dev/null || true' EXIT INT TERM
printf '\n=======================================================\n'
printf '  Frontend (Cliente Web): http://localhost:5080\n'
printf '  Backend (API REST):     http://localhost:5081\n'
printf '  Swagger UI (Docs):      http://localhost:5081/swagger\n'
printf '=======================================================\n\n'
wait -n "$api_pid" "$web_pid"
