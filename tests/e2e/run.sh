#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
PYTHON="${PYTHON:-python3}"
project="packetpeek-e2e-${RANDOM}-$$"
export POSTGRES_PASSWORD="$("$PYTHON" -c 'import secrets; print(secrets.token_hex(20))')"
export JWT_KEY="$("$PYTHON" -c 'import secrets; print(secrets.token_hex(32))')"
export OPERATOR_PASSWORD="E2e-$("$PYTHON" -c 'import secrets; print(secrets.token_hex(16))')!"
export OPERATOR_USERNAME=operator POSTGRES_PORT=0 APP_PORT=0 ENGINE_ENABLED=false
compose=(docker compose -p "$project" -f docker-compose.yml)
cleanup() {
    result=$?
    if (( result != 0 )); then "${compose[@]}" logs --no-color; fi
    "${compose[@]}" down --volumes --remove-orphans
    return "$result"
}
trap cleanup EXIT
"${compose[@]}" up -d --build
base="http://$("${compose[@]}" port app 8080 | tr -d '\r')"
wait_health() {
    for attempt in $(seq 1 90); do
        if curl -fsS "$base/healthz" >/dev/null 2>&1; then return; fi
        sleep 1
    done
    echo 'FAIL: app did not become healthy' >&2; return 1
}
wait_health
login_body="$("$PYTHON" -c 'import json,os; print(json.dumps({"username":os.environ["OPERATOR_USERNAME"],"password":os.environ["OPERATOR_PASSWORD"]}))')"
token="$(curl -fsS -H 'Content-Type: application/json' -d "$login_body" "$base/api/auth/token" | "$PYTHON" -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')"
curl -fsS -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
    -d '{"name":"Replay packets","metric":"packets","grouping":"src_ip","windowSeconds":10,"threshold":1,"active":true}' \
    "$base/api/rules" >/dev/null
export ENGINE_ENABLED=true
"${compose[@]}" up -d --no-deps --force-recreate app
base="http://$("${compose[@]}" port app 8080 | tr -d '\r')"
wait_health
passed=false
for attempt in $(seq 1 60); do
    alerts="$(curl -fsS -H "Authorization: Bearer $token" "$base/api/alerts")"
    flows="$(curl -fsS -H "Authorization: Bearer $token" "$base/api/flows")"
    if printf '%s\n%s\n' "$alerts" "$flows" | "$PYTHON" -c 'import json,sys; a=json.loads(sys.stdin.readline()); f=json.loads(sys.stdin.readline()); sys.exit(0 if len(a)==1 and len(f)==2 and sum(x["packets"] for x in f)==3 else 1)'; then
        passed=true; break
    fi
    sleep 1
done
if [[ "$passed" != true ]]; then echo 'FAIL: expected one alert and two flows' >&2; exit 1; fi
db_count="$("${compose[@]}" exec -T postgres psql -U packetpeek -d packetpeek -tAc 'SELECT count(*) FROM "Alerts";' | tr -d '\r[:space:]')"
[[ "$db_count" == 1 ]]
for page in flows rules alerts swagger/index.html; do curl -fsS "$base/$page" >/dev/null; done
echo 'PASS: Compose replay -> protobuf bridge -> evaluator -> Postgres -> authenticated API; 1 alert, 2 flows, 3 packets; UI and Swagger routes respond'
