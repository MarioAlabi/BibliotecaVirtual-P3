#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
ENV_FILE="${BIBLIOTECA_ENV_FILE:-.env}"
if [ ! -f "$ENV_FILE" ]; then ENV_FILE="$HOME/Documents/Codex/.secrets/biblioteca-virtual.env"; fi
if [ ! -f "$ENV_FILE" ]; then echo 'Configura BIBLIOTECA_ENV_FILE con la ruta de tu archivo privado de conexión.'; exit 1; fi
if docker compose version >/dev/null 2>&1; then
 exec docker compose --env-file "$ENV_FILE" "$@"
elif command -v docker-compose >/dev/null; then
 exec docker-compose --env-file "$ENV_FILE" "$@"
elif [ -x "$HOME/Documents/Codex/.tools/docker-compose" ]; then
 exec "$HOME/Documents/Codex/.tools/docker-compose" --env-file "$ENV_FILE" "$@"
else
 echo 'Instala Docker Compose desde https://docs.docker.com/compose/install/'
 exit 1
fi
