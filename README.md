# AWSAppContainers — Web App API en contenedor

API REST en .NET 10 (Minimal APIs) con base de datos **SQLite**, empaquetada en Docker.

## Base de datos

SQLite **no requiere instalar ni levantar ningún servicio**: la BD es un solo archivo
(`app.db`) que la propia aplicación crea y siembra con datos de ejemplo al arrancar.

- En local: `bin/Debug/net10.0/data/app.db`
- En el contenedor: `/data/app.db` (volumen Docker, sobrevive al borrar el contenedor)
- Se puede cambiar con la variable de entorno `DB_PATH`

### Modelo (normalizado, 3FN)

```
Categorias (Id PK, Nombre UNIQUE)
     1 --- N
Productos  (Id PK, Nombre, Precio, CategoriaId FK -> Categorias.Id  ON DELETE CASCADE)
```

## Respuesta estándar

Todos los endpoints devuelven el mismo schema:

```json
{ "statusCode": 200, "data": [] }
```

## Endpoints

| # | Método | Ruta | Descripción |
|---|--------|------|-------------|
| 1 | GET | `/api/health` | Health check (`/` sirve el frontend) |
| 2 | GET | `/api/categorias` | Lista de categorías |
| 3 | GET | `/api/categorias/{id}` | Categoría con sus productos |
| 4 | POST | `/api/categorias` | Crear categoría |
| 5 | PUT | `/api/categorias/{id}` | Actualizar categoría |
| 6 | DELETE | `/api/categorias/{id}` | Eliminar categoría (cascada) |
| 7 | GET | `/api/productos` | Lista de productos (`?categoriaId=1` opcional) |
| 8 | GET | `/api/productos/{id}` | Un producto |
| 9 | POST | `/api/productos` | Crear producto |
| 10 | PUT | `/api/productos/{id}` | Actualizar producto |
| 11 | DELETE | `/api/productos/{id}` | Eliminar producto |
| 12 | GET | `/api/db/backup` | **Backup** de la BD (descarga un `.db`) |
| 13 | DELETE | `/api/db/vaciar` | **Vaciar** la BD |
| 14 | POST | `/api/auth/registro` | Crear cuenta `{ nombre, email, password }` |
| 15 | POST | `/api/auth/login` | Iniciar sesión; devuelve `{ token, expira, usuario }` |
| 16 | GET | `/api/auth/yo` | Usuario de la sesión actual 🔒 |
| 17 | GET | `/api/usuarios` | Lista de usuarios (`?buscar=` opcional) 🔒 |
| 18 | GET | `/api/usuarios/{id}` | Un usuario 🔒 |
| 19 | POST | `/api/usuarios` | Crear usuario 🔒 |
| 20 | PUT | `/api/usuarios/{id}` | Actualizar usuario (contraseña opcional) 🔒 |
| 21 | DELETE | `/api/usuarios/{id}` | Eliminar usuario (no la propia cuenta) 🔒 |

🔒 = requiere el encabezado `Authorization: Bearer <token>` (JWT, 8 h de vigencia). Sin token responde 401.
Las contraseñas se guardan con hash PBKDF2-SHA256 con sal; nunca se devuelven en las respuestas.

Los errores también usan el schema: `{ "statusCode": 404, "data": [{ "error": "Producto no encontrado" }] }`.
Esto incluye JSON mal formado (400), rutas inexistentes (404), método no permitido (405) y Content-Type incorrecto (415).

Documentación OpenAPI en `/openapi/v1.json`.

## Frontend

HTML, CSS y JavaScript sin frameworks en [wwwroot](wwwroot), servido por la misma app en el puerto 80:

| Página | Qué hace |
|--------|----------|
| `/login.html` | Inicio de sesión (guarda el JWT en `localStorage`) |
| `/registro.html` | Crear cuenta, con confirmación de contraseña |
| `/usuarios.html` | CRUD de usuarios: tabla, búsqueda, crear/editar en un diálogo y eliminar con confirmación |

`/` redirige al login o a usuarios según haya sesión. Si el token expira, la app regresa al login.

## Socket TCP (puerto 6061)

Corre dentro del mismo proceso y contenedor que la API ([TcpServer.cs](TcpServer.cs)).
Un mensaje por línea; la respuesta usa el mismo schema `{ "statusCode", "data" }`.

| Mensaje | Equivale a |
|---------|------------|
| `{insert:{"nombre":"Leche","precio":24.5,"categoriaId":1}}` | `POST /api/productos` (mismo body) |
| `{get:1}` o `{get:{"id":1}}` | `GET /api/productos/1` |

Probar con el cliente incluido:

```bash
.\tcp-client.ps1 -Servidor <ip> '{get:1}' '{insert:{"nombre":"Leche","precio":24.5,"categoriaId":1}}'
```

En EC2 hay que publicar el puerto (`-p 6061:6061`) y abrir el 6061 en el Security Group.

## Pruebas unitarias

Proyecto [Tests/AWSAppContainers.Tests](Tests/AWSAppContainers.Tests) con **xUnit** (el equivalente de Jest en .NET).
Cada prueba levanta la API completa en memoria (`WebApplicationFactory`) con su propia BD SQLite temporal,
así que son independientes entre sí y nunca tocan la BD real.

| Archivo | Cubre |
|---------|-------|
| `CategoriasTests.cs` | GET, GET por id, POST, PUT y DELETE de categorías |
| `ProductosTests.cs` | GET (con filtro), GET por id, POST, PUT y DELETE de productos |
| `GeneralTests.cs` | Health check, frontend, backup, vaciar, rutas y métodos inválidos |
| `UsuariosTests.cs` | Registro, login, protección con JWT y CRUD de usuarios |

Además del caso correcto, cada endpoint prueba los errores típicos del usuario: campos vacíos o faltantes,
valores fuera de rango, ids inexistentes, duplicados, tipos incorrectos, JSON mal formado, Content-Type
equivocado y método HTTP equivocado. Todas las respuestas se validan contra el schema `{ statusCode, data: [] }`.

```bash
dotnet test
```

Para guardar los resultados en un archivo:

```bash
dotnet test --logger "trx;LogFileName=resultados.trx" --results-directory TestResults
```
## Pruebas E2E con Playwright

En [e2e](e2e) (Python + pytest + Playwright), contra la aplicación en ejecución:

| Archivo | Cubre |
|---------|-------|
| `test_api.py` | Backend: health, archivos del frontend, registro, login, JWT, CRUD de usuarios, catálogo |
| `test_frontend.py` | Frontend en el navegador: redirecciones, registro, login, logout, crear/editar/eliminar/buscar usuarios |

```bash
dotnet run                     # en otra terminal; o la app en cualquier URL
cd e2e
pip install -r requirements.txt
python -m playwright install chromium
BASE_URL=http://localhost:5000 python -m pytest -v
```

## Correr en local sin Docker

```bash
dotnet run
```

## Correr con Docker

Opción A — los comandos que pide la práctica:

```bash
docker build -t webapp:latest .
docker run -d -p 8080:80 --name webapp-container webapp:latest
```

Opción B — docker compose (build + volumen para la BD en un solo paso):

```bash
docker compose up -d --build
```

En ambos casos la API queda en <http://localhost:8080>.

## Publicar en Docker Hub

```bash
docker tag webapp:latest <tu-usuario>/webapp:latest
docker login
docker push <tu-usuario>/webapp:latest
```

## Pipeline CI/CD (GitHub Actions → Docker Hub → AWS EC2)

Cada `push` a `main` prueba, empaqueta y despliega la aplicación automáticamente, **solo si pasan todas las
pruebas**. Cada ejecución de pruebas y cada despliegue se registran en OpenObserve.
Definido en [.github/workflows/main.yml](.github/workflows/main.yml).

### Arquitectura

```
 git push (main)
      │
      ▼
┌──────────────────────────── GitHub Actions ────────────────────────────┐
│ 1. test          dotnet test + cobertura (mínimo 70 %)                   │
│ 2. e2e           Playwright: frontend + API contra la app levantada      │
│        │  (en paralelo)                                                  │
│        ├──► 3. report-tests  resultado → OpenObserve (siempre, pase o no)│
│        ▼                                                                 │
│ 4. build-push    docker build → Docker Hub   (solo si 1 y 2 pasaron)     │
│ 5. deploy        SSH a la EC2 → deploy/deploy.sh                         │
│ 6. report-deploy evento de despliegue → OpenObserve                      │
└──────────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
            AWS EC2 (red Docker "observabilidad")
            ├── webapp          API + frontend :80, socket :6061 ──OTLP──┐
            ├── otel-collector  CPU, RAM, red, disco del host ───OTLP────┤
            ├── node-exporter   sensores de temperatura (si existen)     │
            └── openobserve     logs, métricas, trazas, dashboards :5080 ◄┘
```

| Job | Cuándo corre | Qué hace |
|-----|--------------|----------|
| `test` | `push` y `pull_request` a `main` | Pruebas xUnit con cobertura. Falla si la cobertura de líneas es menor al 70 %. Artefacto `coverage-report`. |
| `e2e` | `push` y `pull_request` a `main` | Publica la app, la levanta con una BD limpia y corre la suite de Playwright. Artefacto `playwright-report` (HTML + JUnit + capturas de fallos). |
| `report-tests` | Siempre, después de `test` y `e2e` | Envía a OpenObserve (stream `cicd`) el resultado: éxito o fallo, commit, rama, usuario y enlace al run. |
| `build-push` | Solo `push` a `main`, si `test` **y** `e2e` pasaron | Build del `Dockerfile` y push con las etiquetas `:latest` y `:${{ github.sha }}`. |
| `deploy` | Después de `build-push` | Copia `deploy/` a la EC2 y ejecuta `deploy.sh`: levanta OpenObserve, el collector, node-exporter y la app; importa dashboards y alertas. Verifica que la API y el frontend respondan. |
| `report-deploy` | Después de `deploy` | Registra el despliegue (éxito o fallo) en OpenObserve. |

Si una prueba falla, `report-tests` registra el fallo en OpenObserve y `build-push`/`deploy` no corren:
no hay conexión SSH a la EC2.

### Observabilidad (OpenObserve)

- **Aplicación (OpenTelemetry/OTLP):** trazas de cada petición HTTP con sus consultas a la BD, métricas
  de latencia, peticiones por segundo y códigos de estado por endpoint, y logs estructurados (inicios de
  sesión, altas/bajas de usuarios, excepciones). Se activa con `OTEL_EXPORTER_OTLP_ENDPOINT`.
- **Servidor:** el collector envía CPU, RAM, ancho de banda, disco y carga. La temperatura viene de
  node-exporter; las VMs de EC2 normalmente **no exponen sensores**, así que ese panel puede quedar vacío.
- **Dashboards** (se importan en cada deploy desde `deploy/dashboards/`): *Infraestructura EC2*,
  *Aplicación AWSAppContainers*, *Pipeline CI/CD* y *Alertas operativas*.
- **Alertas:** CPU > 80 %, RAM > 85 %, disco > 90 %, errores 5xx, latencia p95 > 1 s y pipeline fallido.
  Se registran en el stream `alertas` y se ven en el dashboard *Alertas operativas*.

Los dashboards se generan con `python deploy/dashboards/build_dashboards.py deploy/dashboards`.

La cobertura excluye el código autogenerado por el paquete de OpenAPI (carpeta `obj/`).

### Cobertura en local

```bash
dotnet test Tests/AWSAppContainers.Tests --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage -reporttypes:"TextSummary;Html" -filefilters:"-*obj*"
```

El resumen queda en `coverage/Summary.txt` y el reporte navegable en `coverage/index.html`.

### Configuración

#### 1. Docker Hub

1. Crear una cuenta en <https://hub.docker.com>.
2. En *Account settings → Personal access tokens*, generar un token con permiso **Read & Write**.

El repositorio `awsappcontainers` se crea solo en el primer push.

#### 2. Instancia EC2

1. Lanzar una instancia **Ubuntu Server** y descargar su par de claves (`.pem`).
2. En el **Security Group**, abrir estos puertos de entrada:

   | Puerto | Uso |
   |--------|-----|
   | 22 | SSH (GitHub Actions) |
   | 80 | HTTP (API + frontend) |
   | 5080 | OpenObserve (interfaz web y eventos del pipeline desde GitHub Actions) |
   | 6061 | Socket TCP (opcional) |

   La instancia debe tener al menos **2 GB de RAM** (OpenObserve + la app + el collector).

3. Instalar Docker y permitir que el usuario `ubuntu` lo use sin `sudo`. Sin este paso, el despliegue falla con `permission denied ... /var/run/docker.sock`:

   ```bash
   sudo apt update && sudo apt install -y docker.io
   sudo systemctl enable --now docker
   sudo usermod -aG docker ubuntu
   ```

   Cerrar la sesión SSH y volver a entrar para que tome el grupo; `docker ps` debe funcionar sin `sudo`.

#### 3. GitHub Secrets

En *Settings → Secrets and variables → Actions*, crear estos **Repository secrets**:

| Secret | Valor |
|--------|-------|
| `DOCKERHUB_USERNAME` | Usuario de Docker Hub |
| `DOCKERHUB_TOKEN` | Personal Access Token de Docker Hub |
| `EC2_HOST` | IP pública de la EC2 |
| `EC2_USER` | `ubuntu` |
| `EC2_SSH_KEY` | Contenido completo del `.pem`, incluyendo `-----BEGIN ...` y `-----END ...` |
| `OPENOBSERVE_EMAIL` | Correo del administrador de OpenObserve (se crea en el primer deploy) |
| `OPENOBSERVE_PASSWORD` | Contraseña de OpenObserve: 8-128 caracteres con minúscula, mayúscula, número y carácter especial |
| `JWT_KEY` | Texto aleatorio de 32+ caracteres para firmar las sesiones (sin él, las sesiones se pierden en cada deploy) |

> **Seguridad:** el repositorio no contiene contraseñas, IPs, tokens ni claves SSH; todo se lee de GitHub Secrets.

El job `deploy` usa el environment `production`. GitHub lo crea solo, y en *Settings → Environments* se puede pedir aprobación manual antes de cada despliegue.

### Probar el despliegue

1. Cambiar algo visible, por ejemplo el mensaje del health check en [Program.cs](Program.cs):

   ```csharp
   app.MapGet("/api/health", () => Ok(new { servicio = "Servicio de Jorge", estado = "ok" }));
   ```

2. Hacer commit y push:

   ```bash
   git commit -am "Cambia mensaje del health check"
   git push origin main
   ```

3. Seguir el run en la pestaña **Actions**: `test` + `e2e` → `report-tests` → `build-push` → `deploy` → `report-deploy`.
4. Revisar en Docker Hub la nueva etiqueta con el hash del commit.
5. Comprobar la aplicación y OpenObserve:

   ```bash
   curl http://<IP_EC2>/api/health
   ```

   Frontend en `http://<IP_EC2>/` y OpenObserve en `http://<IP_EC2>:5080`.

Para probar el bloqueo del despliegue, introducir un error intencional (por ejemplo, cambiar un texto de
`wwwroot/login.html` que verifican las pruebas): `e2e` falla, el fallo aparece en el dashboard
*Pipeline CI/CD* y la alerta `pipeline_fallido`, y `deploy` no se ejecuta.

### Despliegue manual (sin pipeline)

```bash
docker pull <usuario>/awsappcontainers:latest
docker stop webapp && docker rm webapp
docker run -d --name webapp --restart unless-stopped -p 80:80 -p 6061:6061 -v sqlite-data:/data <usuario>/awsappcontainers:latest
```
