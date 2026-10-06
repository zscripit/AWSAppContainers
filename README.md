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

## Desplegar en EC2 (Ubuntu)

```bash
sudo apt update && sudo apt install -y docker.io
sudo docker run -d -p 8080:80 -p 6061:6061 --name webapp-container -v sqlite-data:/data --restart unless-stopped <tu-usuario>/webapp:latest
```

Abrir el puerto 8080 en el Security Group de la instancia.
