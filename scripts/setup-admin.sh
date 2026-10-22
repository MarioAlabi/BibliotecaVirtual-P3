#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
ENV_FILE="${BIBLIOTECA_ENV_FILE:-.env}"
if [ ! -f "$ENV_FILE" ]; then ENV_FILE="$HOME/Documents/Codex/.secrets/biblioteca-virtual.env"; fi
if [ ! -f "$ENV_FILE" ]; then echo 'Configura BIBLIOTECA_ENV_FILE con el archivo privado de conexión.'; exit 1; fi
set -a
source "$ENV_FILE"
set +a
DOTNET="${DOTNET_BIN:-dotnet}"
if ! command -v "$DOTNET" >/dev/null && [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"; fi
export ConnectionStrings__Library="Server=$DB_HOST;Port=${DB_PORT:-3306};Database=${DB_NAME:-biblioteca_virtual_p3};User=$DB_USER;Password=$DB_PASSWORD;SslMode=Preferred;"
export Bootstrap__AdminEmail="${Bootstrap__AdminEmail:-gerente@biblioteca.com}"
if [ -z "${Bootstrap__AdminPassword:-}" ]; then
 read -r -s -p 'Contraseña del nuevo administrador (Enter si la cuenta ya existe): ' Bootstrap__AdminPassword
 echo
 export Bootstrap__AdminPassword
fi
"$DOTNET" build BibliotecaVirtual.slnx
"$DOTNET" run --no-build --project src/Biblioteca.Api -- --migrate
"$DOTNET" run --no-build --project src/Biblioteca.Api -- --create-admin
