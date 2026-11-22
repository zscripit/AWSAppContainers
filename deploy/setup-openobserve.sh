#!/usr/bin/env bash
# Configura OpenObserve: importa los dashboards y crea las alertas operativas.
# Es idempotente: los dashboards se reemplazan con la versión del repo y las alertas existentes se conservan.
#
# Las alertas se envían al stream "alertas" del propio OpenObserve (destino HTTP por loopback),
# así quedan visibles en el dashboard "Alertas operativas" sin depender de correo o Slack.
set -euo pipefail

: "${OO_EMAIL:?Falta OO_EMAIL}"
: "${OO_PASSWORD:?Falta OO_PASSWORD}"
BASE="${OO_URL:-http://127.0.0.1:5080}"
ORG=default
DIR="$(cd "$(dirname "$0")" && pwd)"
AUTH_B64="$(printf '%s:%s' "$OO_EMAIL" "$OO_PASSWORD" | base64 -w0)"

oo() { curl -sS -u "$OO_EMAIL:$OO_PASSWORD" -H 'Content-Type: application/json' "$@"; }

# ---------- Dashboards ----------
existing="$(oo "$BASE/api/$ORG/dashboards")"
for file in "$DIR"/dashboards/*.json; do
  title="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["title"])' "$file")"
  # Borra la versión anterior (mismo título) para reemplazarla con la del repo.
  for id in $(printf '%s' "$existing" | python3 -c '
import json, sys
t = sys.argv[1]
for d in json.load(sys.stdin).get("dashboards", []):
    if d.get("title") == t:
        print(d["dashboard_id"])' "$title"); do
    oo -o /dev/null -X DELETE "$BASE/api/$ORG/dashboards/$id?folder=default"
  done
  oo -o /dev/null -w "dashboard '$title': %{http_code}\n" -X POST "$BASE/api/$ORG/dashboards" --data-binary "@$file"
done

# ---------- Destino de alertas ----------
oo -o /dev/null -X POST "$BASE/api/$ORG/alerts/templates" -d '{
  "name": "alerta_json", "type": "http", "title": "",
  "body": "[{\"alerta\":\"{alert_name}\",\"stream\":\"{stream_name}\",\"tipo\":\"{alert_type}\",\"inicio\":\"{alert_start_time}\",\"fin\":\"{alert_end_time}\"}]"
}' || true
oo -o /dev/null -X POST "$BASE/api/$ORG/alerts/destinations" -d "{
  \"name\": \"stream_alertas\", \"type\": \"http\", \"method\": \"post\", \"skip_tls_verify\": true,
  \"url\": \"http://127.0.0.1:5080/api/$ORG/alertas/_json\", \"template\": \"alerta_json\",
  \"headers\": {\"Authorization\": \"Basic $AUTH_B64\", \"Content-Type\": \"application/json\"}
}" || true

# ---------- Alertas ----------
alerts="$(oo "$BASE/api/v2/$ORG/alerts")"
has_alert() { printf '%s' "$alerts" | grep -q "\"name\":\"$1\""; }

# Una alerta solo se puede crear sobre un stream que ya existe. En un OpenObserve recién
# instalado las métricas tardan unos segundos en llegar, así que se espera hasta WAIT_SECONDS.
WAIT_UNTIL=$(( $(date +%s) + ${WAIT_SECONDS:-120} ))
wait_stream() {  # tipo nombre
  while true; do
    oo "$BASE/api/$ORG/streams?type=$1" | grep -q "\"name\":\"$2\"" && return 0
    [ "$(date +%s)" -ge "$WAIT_UNTIL" ] && return 1
    sleep 5
  done
}

promql_alert() {  # nombre stream "consulta" umbral descripción
  local name="$1" stream="$2" query="$3" value="$4" description="$5"
  has_alert "$name" && { echo "alerta '$name': ya existe"; return; }
  wait_stream metrics "$stream" || { echo "alerta '$name': omitida, aún no llegan datos a '$stream' (se crea en el siguiente deploy)"; return; }
  python3 - "$name" "$stream" "$query" "$value" "$description" > /tmp/oo-alert.json <<'PY'
import json, sys
name, stream, query, value, description = sys.argv[1:]
print(json.dumps({
    "name": name, "description": description, "enabled": True, "is_real_time": False,
    "stream_type": "metrics", "stream_name": stream,
    "query_condition": {"type": "promql", "promql": query, "sql": None, "conditions": None,
                        "promql_condition": {"column": "value", "operator": ">", "value": float(value)}},
    "trigger_condition": {"period": 5, "operator": ">=", "threshold": 1, "frequency": 1,
                          "frequency_type": "minutes", "silence": 10},
    "destinations": ["stream_alertas"],
}))
PY
  oo -o /dev/null -w "alerta '$name': %{http_code}\n" -X POST "$BASE/api/v2/$ORG/alerts" --data-binary @/tmp/oo-alert.json
}

promql_alert cpu_alta system_cpu_utilization '100 * (1 - avg(system_cpu_utilization{state="idle"}))' 80 \
  "CPU del servidor arriba de 80%"
promql_alert ram_alta system_memory_utilization '100 * sum(system_memory_utilization{state="used"})' 85 \
  "Memoria RAM del servidor arriba de 85%"
promql_alert disco_lleno system_filesystem_utilization '100 * max(system_filesystem_utilization)' 90 \
  "Sistema de archivos arriba de 90%"
promql_alert errores_5xx http_server_request_duration_count \
  'sum(rate(http_server_request_duration_count{http_response_status_code=~"5.."}[5m]))' 0 \
  "La aplicación respondió errores HTTP 5xx"
promql_alert latencia_alta http_server_request_duration_bucket \
  'histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_bucket[5m])))' 1 \
  "Latencia p95 de la aplicación mayor a 1 segundo"

# Pipeline fallido: alerta SQL sobre el stream "cicd" (existe desde la primera ejecución reportada).
if has_alert pipeline_fallido; then
  echo "alerta 'pipeline_fallido': ya existe"
elif ! WAIT_UNTIL=0 wait_stream logs cicd; then
  echo "alerta 'pipeline_fallido': omitida, el stream 'cicd' aún no existe (se crea en el siguiente deploy)"
else
  oo -o /dev/null -w "alerta 'pipeline_fallido': %{http_code}\n" -X POST "$BASE/api/v2/$ORG/alerts" -d '{
    "name": "pipeline_fallido", "description": "Una ejecución del pipeline CI/CD falló",
    "enabled": true, "is_real_time": false, "stream_type": "logs", "stream_name": "cicd",
    "query_condition": {"type": "sql", "sql": "SELECT _timestamp, branch, commit_sha, actor FROM \"cicd\" WHERE status = '"'"'failure'"'"'",
                        "conditions": null, "promql": null, "promql_condition": null},
    "trigger_condition": {"period": 5, "operator": ">=", "threshold": 1, "frequency": 1,
                          "frequency_type": "minutes", "silence": 5},
    "destinations": ["stream_alertas"]
  }'
fi
