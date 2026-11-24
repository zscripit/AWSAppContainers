"""Genera los JSON de dashboards de OpenObserve (formato v5) para deploy/dashboards."""
import json
import sys
from pathlib import Path

OUT = Path(sys.argv[1])


def promql(i, title, query, legend, unit="default", ptype="line", x=0, y=0, w=24, h=9, decimals=2):
    return {
        "id": f"Panel_{i}",
        "type": ptype,
        "title": title,
        "description": "",
        "config": {"show_legends": True, "legends_position": None, "unit": unit, "decimals": decimals,
                   "axis_border_show": False, "connect_nulls": True},
        "queryType": "promql",
        "queries": [{
            "query": query,
            "customQuery": True,
            "fields": {"stream": "", "stream_type": "metrics", "x": [], "y": [], "z": [], "filter": {"filterType": "group", "logicalOperator": "AND", "conditions": []}},
            "config": {"promql_legend": legend, "layer_type": "scatter", "weight_fixed": 1, "limit": 0,
                       "min": 0, "max": 100},
        }],
        "layout": {"x": x, "y": y, "w": w, "h": h, "i": i},
        "htmlContent": "",
        "markdownContent": "",
    }


def field(col):
    return {"label": col, "alias": col, "column": col, "color": None, "aggregationFunction": None}


def sql(i, title, query, stream, stream_type, ptype="table", x=0, y=0, w=48, h=10, xcols=(), ycols=()):
    panel = {
        "id": f"Panel_{i}",
        "type": ptype,
        "title": title,
        "description": "",
        "config": {"show_legends": True, "legends_position": None, "decimals": 2},
        "queryType": "sql",
        "queries": [{
            "query": query,
            "customQuery": True,
            "fields": {"stream": stream, "stream_type": stream_type, "x": [], "y": [], "z": [], "filter": {"filterType": "group", "logicalOperator": "AND", "conditions": []}},
            "config": {"promql_legend": "", "layer_type": "scatter", "weight_fixed": 1, "limit": 0},
        }],
        "layout": {"x": x, "y": y, "w": w, "h": h, "i": i},
        "htmlContent": "",
        "markdownContent": "",
    }
    # Los paneles SQL arman columnas/ejes a partir de estos campos.
    panel["queries"][0]["fields"]["x"] = [field(c) for c in xcols]
    panel["queries"][0]["fields"]["y"] = [field(c) for c in ycols]
    return panel


def dashboard(title, description, panels):
    return {"version": 5, "title": title, "description": description,
            "tabs": [{"tabId": "default", "name": "Default", "panels": panels}]}


infra = dashboard("Infraestructura EC2", "CPU, memoria RAM, ancho de banda, disco y temperatura del servidor", [
    promql(1, "CPU en uso (%)", '100 * (1 - avg(system_cpu_utilization{state="idle"}))', "CPU", "percent", x=0, y=0),
    promql(2, "Memoria RAM en uso (%)", '100 * sum(system_memory_utilization{state="used"})', "RAM", "percent", x=24, y=0),
    promql(3, "Memoria RAM por estado (bytes)", "sum by (state) (system_memory_usage)", "{state}", "bytes", x=0, y=9),
    promql(4, "Ancho de banda (bytes/s)", "sum by (direction) (rate(system_network_io[1m]))", "{direction}",
           "bytes/second", x=24, y=9),
    promql(5, "Carga promedio", "system_cpu_load_average_1m", "1 min", x=0, y=18),
    promql(6, "Disco: lectura/escritura (bytes/s)", "sum by (direction) (rate(system_disk_io[1m]))", "{direction}",
           "bytes/second", x=24, y=18),
    promql(7, "Uso del sistema de archivos (%)", "100 * max by (mountpoint) (system_filesystem_utilization)",
           "{mountpoint}", "percent", x=0, y=27),
    # Un sensor no implementado reporta el cero absoluto (-273.15 °C); en EC2 lo hace el disco NVMe
    # de EBS. Se descartan las lecturas imposibles para no graficar datos falsos.
    promql(8, "Temperatura (°C)",
           "max by (chip, sensor) (node_hwmon_temp_celsius > -50)"
           " or max by (zone) (node_thermal_zone_temp > -50)",
           "{chip} {sensor}{zone}", "celsius", x=24, y=27),
])

app = dashboard("Aplicación AWSAppContainers", "Latencia, peticiones por segundo y errores HTTP por endpoint", [
    promql(1, "Peticiones por segundo por endpoint",
           "sum by (http_route) (rate(http_server_request_duration_count[1m]))", "{http_route}", x=0, y=0),
    promql(2, "Latencia p95 por endpoint (s)",
           "histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_bucket[5m])))",
           "{http_route}", "seconds", x=24, y=0, decimals=3),
    promql(3, "Peticiones por código de estado",
           "sum by (http_response_status_code) (rate(http_server_request_duration_count[1m]))",
           "{http_response_status_code}", x=0, y=9),
    promql(4, "Tasa de error HTTP 5xx (%)",
           '100 * (sum(rate(http_server_request_duration_count{http_response_status_code=~"5.."}[5m])) or vector(0))'
           " / sum(rate(http_server_request_duration_count[5m]))", "5xx", "percent", x=24, y=9),
    promql(5, "Tasa de error HTTP 4xx por endpoint (%)",
           '100 * sum by (http_route) (rate(http_server_request_duration_count{http_response_status_code=~"4.."}[5m]))'
           " / sum by (http_route) (rate(http_server_request_duration_count[5m]))", "{http_route}", "percent",
           x=0, y=18),
    promql(6, "Latencia promedio por endpoint (s)",
           "sum by (http_route) (rate(http_server_request_duration_sum[5m]))"
           " / sum by (http_route) (rate(http_server_request_duration_count[5m]))", "{http_route}", "seconds",
           x=24, y=18, decimals=3),
    # severity_number sigue el estándar de OpenTelemetry: 13-16 = WARN, 17 o más = ERROR/FATAL.
    sql(7, "Últimos errores y advertencias de la aplicación",
        "SELECT _timestamp, severity, body, exception_type, exception_message FROM \"default\" "
        "WHERE severity_number >= 13 ORDER BY _timestamp DESC LIMIT 50", "default", "logs", x=0, y=27,
        xcols=("_timestamp", "severity", "body", "exception_type", "exception_message")),
])

cicd = dashboard("Pipeline CI/CD", "Resultado de las pruebas y despliegues registrados por GitHub Actions", [
    sql(1, "Ejecuciones del pipeline",
        "SELECT _timestamp, event, status, branch, commit_sha, actor, run_url FROM \"cicd\" "
        "ORDER BY _timestamp DESC LIMIT 100", "cicd", "logs", x=0, y=0, h=12,
        xcols=("_timestamp", "event", "status", "branch", "commit_sha", "actor", "run_url")),
    sql(2, "Resultados por estado", "SELECT status, count(*) AS total FROM \"cicd\" GROUP BY status",
        "cicd", "logs", ptype="pie", x=0, y=12, w=24, xcols=("status",), ycols=("total",)),
    sql(3, "Despliegues por día",
        "SELECT histogram(_timestamp, '1 day') AS dia, count(*) AS despliegues FROM \"cicd\" "
        "WHERE event = 'deploy' GROUP BY dia ORDER BY dia", "cicd", "logs", ptype="bar", x=24, y=12, w=24,
        xcols=("dia",), ycols=("despliegues",)),
])

alertas = dashboard("Alertas operativas", "Alertas disparadas por OpenObserve (CPU, RAM, disco, errores, latencia y pipeline)", [
    sql(1, "Alertas disparadas", "SELECT _timestamp, alerta, stream, inicio, fin FROM \"alertas\" "
        "ORDER BY _timestamp DESC LIMIT 100", "alertas", "logs", x=0, y=0, h=12,
        xcols=("_timestamp", "alerta", "stream", "inicio", "fin")),
    sql(2, "Alertas por tipo", "SELECT alerta, count(*) AS veces FROM \"alertas\" GROUP BY alerta",
        "alertas", "logs", ptype="bar", x=0, y=12, w=24, xcols=("alerta",), ycols=("veces",)),
    sql(3, "Alertas por hora",
        "SELECT histogram(_timestamp, '1 hour') AS hora, count(*) AS alertas FROM \"alertas\" GROUP BY hora ORDER BY hora",
        "alertas", "logs", ptype="line", x=24, y=12, w=24, xcols=("hora",), ycols=("alertas",)),
])

OUT.mkdir(parents=True, exist_ok=True)
for name, d in [("infraestructura", infra), ("aplicacion", app), ("cicd", cicd), ("alertas", alertas)]:
    (OUT / f"{name}.json").write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
    print("escrito", name)
