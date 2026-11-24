#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
PYTHON="${PYTHON:-python3}"
project="packetpeek-e2e-${RANDOM}-$$"
export POSTGRES_PASSWORD="$("$PYTHON" -c 'import secrets; print(secrets.token_hex(20))')"
export JWT_KEY="$("$PYTHON" -c 'import secrets; print(secrets.token_hex(32))')"
export OPERATOR_PASSWORD="E2e-$("$PYTHON" -c 'import secrets; print(secrets.token_hex(16))')!"
export GRAFANA_ADMIN_PASSWORD="Grafana-$("$PYTHON" -c 'import secrets; print(secrets.token_hex(16))')!"
export OPERATOR_USERNAME=operator POSTGRES_PORT=0 APP_PORT=0 PROMETHEUS_PORT=0 GRAFANA_PORT=0 LOKI_PORT=0 ALLOY_PORT=0 ENGINE_ENABLED=false
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
curl -fsS "$base/health/live" >/dev/null
curl -fsS "$base/health/ready" >/dev/null
initial_metrics="$(curl -fsS "$base/metrics")"
grep -q '^packetpeek_packets_processed_total' <<<"$initial_metrics"
grep -q '^packetpeek_engine_capture_expected 0' <<<"$initial_metrics"
grep -q '^packetpeek_channel_depth' <<<"$initial_metrics"
grep -q '^http_requests_received_total' <<<"$initial_metrics"
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
replay_metrics="$(curl -fsS "$base/metrics")"
grep -q '^packetpeek_engine_capture_expected 0' <<<"$replay_metrics"
grep -q '^packetpeek_packets_processed_total 3' <<<"$replay_metrics"
grep -q '^packetpeek_flows_created_total 2' <<<"$replay_metrics"
grep -q '^packetpeek_alerts_triggered_total 1' <<<"$replay_metrics"
grep -q '^packetpeek_rule_evaluations_total 3' <<<"$replay_metrics"
grep -q '^packetpeek_rule_evaluation_duration_seconds_count 3' <<<"$replay_metrics"
grep -q '^packetpeek_db_operation_duration_seconds_count' <<<"$replay_metrics"
prometheus="http://$("${compose[@]}" port prometheus 9090 | tr -d '\r')"
prometheus_scraped=false
for attempt in $(seq 1 60); do
    if curl -fsSG --data-urlencode 'query=up{job="packetpeek"}' "$prometheus/api/v1/query" |
        "$PYTHON" -c 'import json,sys; d=json.load(sys.stdin)["data"]["result"]; sys.exit(0 if d and d[0]["value"][1] == "1" else 1)'; then
        prometheus_scraped=true; break
    fi
    sleep 1
done
[[ "$prometheus_scraped" == true ]] || { echo 'FAIL: Prometheus did not scrape PacketPeek' >&2; exit 1; }
grafana="http://$("${compose[@]}" port grafana 3000 | tr -d '\r')"
grafana_ready=false
for attempt in $(seq 1 60); do
    if curl -fsS "$grafana/api/health" 2>/dev/null |
        "$PYTHON" -c 'import json,sys; sys.exit(0 if json.load(sys.stdin).get("database") == "ok" else 1)' 2>/dev/null; then
        grafana_ready=true; break
    fi
    sleep 1
done
[[ "$grafana_ready" == true ]] || { echo 'FAIL: Grafana did not become healthy' >&2; exit 1; }
loki="http://$("${compose[@]}" port loki 3100 | tr -d '\r')"
loki_received_logs=false
query_end="$(date +%s)"
query_start="$((query_end - 300))"
for attempt in $(seq 1 60); do
    if curl -fsSG --data-urlencode 'query={application="packetpeek",service="app"}' --data-urlencode "start=$query_start" --data-urlencode "end=$query_end" --data-urlencode 'limit=20' "$loki/loki/api/v1/query_range" 2>/dev/null |
        "$PYTHON" -c 'import json,sys; d=json.load(sys.stdin)["data"]["result"]; sys.exit(0 if any("@t" in line and "@mt" in line for stream in d for _,line in stream["values"]) else 1)' 2>/dev/null; then
        loki_received_logs=true; break
    fi
    sleep 1
done
[[ "$loki_received_logs" == true ]] || { echo 'FAIL: Loki did not receive PacketPeek stack logs' >&2; exit 1; }
for page in flows rules alerts swagger/index.html; do curl -fsS "$base/$page" >/dev/null; done
echo 'PASS: Compose replay -> protobuf bridge -> evaluator -> Postgres -> authenticated API; Prometheus scrape, Grafana health, Loki log ingestion; 1 alert, 2 flows, 3 packets; UI and Swagger routes respond'
