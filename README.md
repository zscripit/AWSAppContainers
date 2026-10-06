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
| 1 | GET | `/` | Health check |
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

Los errores también usan el schema: `{ "statusCode": 404, "data": [{ "error": "Producto no encontrado" }] }`.
Esto incluye JSON mal formado (400), rutas inexistentes (404), método no permitido (405) y Content-Type incorrecto (415).

Documentación OpenAPI en `/openapi/v1.json`.

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
| `GeneralTests.cs` | Health check, backup, vaciar, rutas y métodos inválidos |

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

Cada `push` a `main` prueba, empaqueta y despliega la API automáticamente.
Definido en [.github/workflows/main.yml](.github/workflows/main.yml).

### Arquitectura

```
 git push (main)
      │
      ▼
┌─────────────────────── GitHub Actions ───────────────────────┐
│ 1. test         dotnet test + cobertura (mínimo 70 %)          │
│        │                                                       │
│        ▼                                                       │
│ 2. build-push   docker build → Docker Hub                      │
│                 tags :latest y :<commit sha>                   │
│        │                                                       │
│        ▼                                                       │
│ 3. deploy       SSH a la EC2 → docker pull → reemplaza         │
│                 el contenedor → verifica que la API responda   │
└───────────────────────────────────────────────────────────────┘
      │                                    │
      ▼                                    ▼
 Docker Hub  ───── docker pull ─────►  AWS EC2 (Ubuntu + Docker)
 <usuario>/awsappcontainers            contenedor "webapp"
                                       puerto 80 → API
                                       volumen sqlite-data → /data
```

| Job | Cuándo corre | Qué hace |
|-----|--------------|----------|
| `test` | `push` y `pull_request` a `main` | Pruebas xUnit con cobertura (coverlet + ReportGenerator). El resumen se muestra en el log y en el *Summary* del run. Falla si la cobertura de líneas es menor al 70 %. El reporte HTML se sube como artefacto `coverage-report`. |
| `build-push` | Solo `push` a `main`, si `test` pasó | Login en Docker Hub con un Personal Access Token, build del `Dockerfile` y push con las etiquetas `:latest` y `:${{ github.sha }}`. |
| `deploy` | Después de `build-push` | Por SSH en la EC2: descarga la imagen nueva, detiene y elimina el contenedor anterior, levanta el nuevo en el puerto 80 y limpia imágenes viejas. Después hace peticiones a `/api/categorias` hasta que la API responde. |

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
   | 80 | HTTP (API) |
   | 6061 | Socket TCP (opcional) |

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

> **Seguridad:** el repositorio no contiene contraseñas, IPs, tokens ni claves SSH; todo se lee de GitHub Secrets.

El job `deploy` usa el environment `production`. GitHub lo crea solo, y en *Settings → Environments* se puede pedir aprobación manual antes de cada despliegue.

### Probar el despliegue

1. Cambiar algo visible, por ejemplo el mensaje del health check en [Program.cs](Program.cs):

   ```csharp
   app.MapGet("/", () => Ok(new { servicio = "Servicio de Jorge", estado = "ok" }));
   ```

2. Hacer commit y push:

   ```bash
   git commit -am "Cambia mensaje del health check"
   git push origin main
   ```

3. Seguir el run en la pestaña **Actions**: `test` → `build-push` → `deploy`.
4. Revisar en Docker Hub la nueva etiqueta con el hash del commit.
5. Comprobar la API:

   ```bash
   curl http://<IP_EC2>/
   curl http://<IP_EC2>/api/categorias
   ```

### Despliegue manual (sin pipeline)

```bash
docker pull <usuario>/awsappcontainers:latest
docker stop webapp && docker rm webapp
docker run -d --name webapp --restart unless-stopped -p 80:80 -p 6061:6061 -v sqlite-data:/data <usuario>/awsappcontainers:latest
```
