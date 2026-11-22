"""Backend: endpoints REST probados con el APIRequestContext de Playwright."""
import pytest

from conftest import PASSWORD, unique_email


def data(resp):
    body = resp.json()
    assert body["statusCode"] == resp.status
    assert isinstance(body["data"], list)
    return body["data"]


# ---------- Salud y frontend ----------
def test_health(api):
    resp = api.get("/api/health")
    assert resp.status == 200
    assert data(resp)[0]["estado"] == "ok"


@pytest.mark.parametrize("path", ["/", "/login.html", "/registro.html", "/usuarios.html", "/productos.html", "/css/styles.css",
                                  "/js/api.js", "/js/usuarios.js", "/js/productos.js", "/js/layout.js"])
def test_archivos_del_frontend(api, path):
    assert api.get(path).status == 200


def test_ruta_inexistente_404(api):
    resp = api.get("/api/no-existe")
    assert resp.status == 404
    assert data(resp)[0]["error"] == "Ruta no encontrada"


# ---------- Registro y login ----------
def test_registro_valido(api):
    email = unique_email()
    resp = api.post("/api/auth/registro", data={"nombre": "Ana", "email": email, "password": PASSWORD})
    assert resp.status == 201
    usuario = data(resp)[0]
    assert usuario["email"] == email
    assert "passwordHash" not in usuario


def test_registro_duplicado_409(api, user):
    resp = api.post("/api/auth/registro", data={"nombre": "Otro", "email": user["email"], "password": PASSWORD})
    assert resp.status == 409


@pytest.mark.parametrize("body, message", [
    ({"nombre": "", "email": "a@b.com", "password": PASSWORD}, "El nombre es obligatorio"),
    ({"nombre": "Ana", "email": "no-es-correo", "password": PASSWORD}, "El correo no tiene un formato válido"),
    ({"nombre": "Ana", "email": "a@b.com", "password": "corta1"}, "La contraseña debe tener al menos 8 caracteres"),
    ({"nombre": "Ana", "email": "a@b.com", "password": "sinnumeros"}, "La contraseña debe incluir letras y números"),
])
def test_registro_invalido_400(api, body, message):
    resp = api.post("/api/auth/registro", data=body)
    assert resp.status == 400
    assert data(resp)[0]["error"] == message


def test_login_correcto(api, user):
    resp = api.post("/api/auth/login", data={"email": user["email"], "password": PASSWORD})
    assert resp.status == 200
    result = data(resp)[0]
    assert result["token"]
    assert result["usuario"]["id"] == user["id"]


@pytest.mark.parametrize("password", ["Incorrecta1", ""])
def test_login_incorrecto(api, user, password):
    resp = api.post("/api/auth/login", data={"email": user["email"], "password": password})
    assert resp.status in (400, 401)


def test_yo(api, user, auth_headers):
    resp = api.get("/api/auth/yo", headers=auth_headers)
    assert resp.status == 200
    assert data(resp)[0]["email"] == user["email"]


# ---------- CRUD protegido ----------
@pytest.mark.parametrize("method, path", [("get", "/api/usuarios"), ("get", "/api/usuarios/1"),
                                          ("delete", "/api/usuarios/1"), ("get", "/api/auth/yo")])
def test_sin_token_401(api, method, path):
    assert getattr(api, method)(path).status == 401


def test_token_invalido_401(api):
    assert api.get("/api/usuarios", headers={"Authorization": "Bearer falso"}).status == 401


def test_crud_completo(api, auth_headers):
    email = unique_email("crud")
    creado = api.post("/api/usuarios", headers=auth_headers,
                      data={"nombre": "Crud", "email": email, "password": PASSWORD})
    assert creado.status == 201
    uid = data(creado)[0]["id"]

    leido = api.get(f"/api/usuarios/{uid}", headers=auth_headers)
    assert data(leido)[0]["email"] == email

    editado = api.put(f"/api/usuarios/{uid}", headers=auth_headers,
                      data={"nombre": "Crud Editado", "email": email, "password": ""})
    assert data(editado)[0]["nombre"] == "Crud Editado"

    borrado = api.delete(f"/api/usuarios/{uid}", headers=auth_headers)
    assert borrado.status == 200
    assert api.get(f"/api/usuarios/{uid}", headers=auth_headers).status == 404


def test_listar_y_buscar(api, user, auth_headers):
    todos = data(api.get("/api/usuarios", headers=auth_headers))
    assert any(u["id"] == user["id"] for u in todos)
    filtrados = data(api.get("/api/usuarios", headers=auth_headers, params={"buscar": user["email"]}))
    assert [u["id"] for u in filtrados] == [user["id"]]


def test_no_puede_eliminarse_a_si_mismo(api, user, auth_headers):
    resp = api.delete(f"/api/usuarios/{user['id']}", headers=auth_headers)
    assert resp.status == 400


# ---------- Endpoints existentes (catálogo) ----------
@pytest.mark.parametrize("path", ["/api/categorias", "/api/productos"])
def test_catalogo_lista(api, path):
    resp = api.get(path)
    assert resp.status == 200
    assert isinstance(data(resp), list)


def test_categoria_crear_y_eliminar(api):
    nombre = f"E2E {unique_email('cat')}"[:90]
    creada = api.post("/api/categorias", data={"nombre": nombre})
    assert creada.status == 201
    cid = data(creada)[0]["id"]
    assert api.delete(f"/api/categorias/{cid}").status == 200
