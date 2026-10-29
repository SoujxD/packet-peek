#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
PYTHON="${PYTHON:-python3}"
project="packetpeek-ui-${RANDOM}-$$"
root="$PWD"
if [[ "${OSTYPE:-}" == msys* || "${OSTYPE:-}" == cygwin* ]]; then
    root="$(pwd -W)"
    export MSYS_NO_PATHCONV=1
fi
export POSTGRES_PASSWORD="$("$PYTHON" -c 'import secrets; print(secrets.token_hex(20))')"
export JWT_KEY="$("$PYTHON" -c 'import secrets; print(secrets.token_hex(32))')"
export OPERATOR_PASSWORD="Ui-$("$PYTHON" -c 'import secrets; print(secrets.token_hex(16))')!"
export OPERATOR_USERNAME=operator POSTGRES_PORT=0 APP_PORT=0 ENGINE_ENABLED=false
compose=(docker compose -p "$project" -f docker-compose.yml)
cleanup() {
    result=$?
    if (( result != 0 )); then "${compose[@]}" logs --no-color; fi
    "${compose[@]}" down --volumes --remove-orphans
    return "$result"
}
trap cleanup EXIT
docker build -f tests/ui/Dockerfile -t packetpeek-browser-tests:local .
"${compose[@]}" up -d --build
wait_health() {
    local base="http://$("${compose[@]}" port app 8080 | tr -d '\r')"
    for attempt in $(seq 1 90); do
        if curl -fsS "$base/healthz" >/dev/null 2>&1; then return; fi
        sleep 1
    done
    echo 'FAIL: UI test app did not become healthy' >&2; return 1
}
browser() {
    docker run --rm --network "${project}_default" -v "$root:/work" \
        -e OPERATOR_USERNAME -e OPERATOR_PASSWORD -e PACKETPEEK_URL=http://app:8080 \
        packetpeek-browser-tests:local python tests/ui/smoke.py "$1"
}
wait_health
browser prepare
export ENGINE_ENABLED=true
"${compose[@]}" up -d --no-deps --force-recreate app
wait_health
browser verify
