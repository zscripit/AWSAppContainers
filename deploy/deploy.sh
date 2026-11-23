#!/usr/bin/env bash
# Despliega en el servidor (EC2) la aplicación y su stack de observabilidad:
#   openobserve     plataforma de logs, métricas, trazas y dashboards (puerto 5080)
#   node-exporter   expone sensores de temperatura del host
#   otel-collector  métricas del host (CPU, RAM, red, disco) hacia OpenObserve
#   webapp          la API + frontend, instrumentada con OpenTelemetry (puertos 80 y 6061)
#
# Variables requeridas: IMAGE, OO_EMAIL, OO_PASSWORD. Opcionales: JWT_KEY, APP_VERSION.
set -euo pipefail

: "${IMAGE:?Falta IMAGE}"
: "${OO_EMAIL:?Falta OO_EMAIL}"
: "${OO_PASSWORD:?Falta OO_PASSWORD}"
DIR="$(cd "$(dirname "$0")" && pwd)"
NET=observabilidad
OO_AUTH="$(printf '%s:%s' "$OO_EMAIL" "$OO_PASSWORD" | base64 -w0)"

docker network inspect "$NET" >/dev/null 2>&1 || docker network create "$NET"

# El disco de la instancia es chico: antes de descargar nada se borran las imágenes que
# ningún contenedor usa (las de despliegues anteriores). No toca volúmenes ni datos.
docker image prune -a -f >/dev/null || true
df -h / | tail -1

# Solo crea un contenedor si no existe, si su imagen cambió o si cambió su configuración;
# así OpenObserve no se reinicia en cada deploy pero sí toma los cambios de este script.
ensure() {
  # Uso: ensure nombre imagen [opciones de docker run...] [-- argumentos del contenedor...]
  local name="$1" image="$2"; shift 2
  local opts=() args=()
  while [ $# -gt 0 ] && [ "$1" != "--" ]; do opts+=("$1"); shift; done
  [ "${1:-}" = "--" ] && shift && args=("$@")
  docker pull -q "$image" >/dev/null
  local running wanted config current
  running="$(docker inspect -f '{{.Image}}' "$name" 2>/dev/null || true)"
  wanted="$(docker image inspect -f '{{.Id}}' "$image")"
  config="$(printf '%s\n' "$image" "${opts[@]}" "${args[@]}" | sha256sum | cut -c1-16)"
  current="$(docker inspect -f '{{index .Config.Labels "deploy.config"}}' "$name" 2>/dev/null || true)"
  if [ "$running" != "$wanted" ] || [ "$current" != "$config" ] \
     || [ "$(docker inspect -f '{{.State.Running}}' "$name" 2>/dev/null)" != "true" ]; then
    docker rm -f "$name" >/dev/null 2>&1 || true
    docker run -d --name "$name" --network "$NET" --restart unless-stopped --label "deploy.config=$config" \
      "${opts[@]}" "$image" "${args[@]}" >/dev/null
    echo "-> $name (re)creado"
  else
    echo "-> $name sin cambios"
  fi
}

# ---------- OpenObserve ----------
ensure openobserve public.ecr.aws/zinclabs/openobserve:latest \
  -p 5080:5080 \
  -e ZO_ROOT_USER_EMAIL="$OO_EMAIL" \
  -e ZO_ROOT_USER_PASSWORD="$OO_PASSWORD" \
  -e ZO_DATA_DIR=/data \
  -e ZO_SSRF_ALLOW_LOOPBACK=true \
  -e ZO_COMPACT_DATA_RETENTION_DAYS=3 \
  -v openobserve-data:/data

echo "Esperando a OpenObserve..."
for _ in $(seq 1 60); do curl -fs http://127.0.0.1:5080/healthz >/dev/null && break; sleep 3; done
curl -fs http://127.0.0.1:5080/healthz >/dev/null || { echo "OpenObserve no respondió"; docker logs --tail 40 openobserve; exit 1; }

# ---------- Métricas del servidor ----------
ensure node-exporter prom/node-exporter:latest \
  --pid host -v /:/host:ro,rslave \
  -- --path.rootfs=/host

# La configuración del collector cambia con el repo: se recrea siempre.
docker rm -f otel-collector >/dev/null 2>&1 || true
docker run -d --name otel-collector --network "$NET" --restart unless-stopped \
  --pid host -v /:/hostfs:ro \
  -v "$DIR/otel-collector.yaml:/etc/otelcol-contrib/config.yaml:ro" \
  -e OO_OTLP_ENDPOINT=http://openobserve:5080/api/default \
  -e OO_AUTH="$OO_AUTH" \
  otel/opentelemetry-collector-contrib:latest >/dev/null
echo "-> otel-collector recreado"

# ---------- Aplicación ----------
docker pull "$IMAGE"
docker rm -f webapp >/dev/null 2>&1 || true
for p in 80 6061; do docker ps -aq --filter "publish=$p" | xargs -r docker rm -f; done
docker run -d --name webapp --network "$NET" --restart unless-stopped \
  -p 80:80 -p 6061:6061 \
  -v sqlite-data:/data \
  -e JWT_KEY="${JWT_KEY:-}" \
  -e APP_VERSION="${APP_VERSION:-}" \
  -e OTEL_SERVICE_NAME=awsappcontainers \
  -e OTEL_EXPORTER_OTLP_ENDPOINT=http://openobserve:5080/api/default \
  -e OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
  -e OTEL_EXPORTER_OTLP_HEADERS="Authorization=Basic $OO_AUTH" \
  -e OTEL_METRIC_EXPORT_INTERVAL=30000 \
  "$IMAGE"

# Unas peticiones de calentamiento hacen que la app publique sus primeras métricas HTTP,
# necesarias para crear las alertas de errores y latencia.
for _ in $(seq 1 30); do curl -fs -o /dev/null http://127.0.0.1/api/health && break; sleep 2; done
for _ in $(seq 1 10); do curl -s -o /dev/null http://127.0.0.1/api/health; curl -s -o /dev/null http://127.0.0.1/; done

# ---------- Dashboards y alertas ----------
OO_EMAIL="$OO_EMAIL" OO_PASSWORD="$OO_PASSWORD" bash "$DIR/setup-openobserve.sh" || echo "Aviso: no se pudieron importar los dashboards"

sleep 5
docker ps -a --filter network="$NET" --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
docker logs --tail 20 webapp
docker image prune -a -f >/dev/null || true
df -h / | tail -1
