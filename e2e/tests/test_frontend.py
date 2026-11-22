"""Frontend: login, registro y CRUD de usuarios desde el navegador."""
import re

import pytest
from playwright.sync_api import expect

from conftest import PASSWORD, unique_email


# ---------- Navegación sin sesión ----------
def test_raiz_sin_sesion_va_al_login(page):
    page.goto("/")
    expect(page).to_have_url(re.compile("/login.html"))


def test_usuarios_sin_sesion_va_al_login(page):
    page.goto("/usuarios.html")
    expect(page).to_have_url(re.compile("/login.html"))


def test_login_elementos(page):
    page.goto("/login.html")
    expect(page.get_by_role("heading", name="Iniciar sesión")).to_be_visible()
    expect(page.get_by_test_id("email")).to_be_visible()
    expect(page.get_by_test_id("password")).to_have_attribute("type", "password")


def test_link_a_registro(page):
    page.goto("/login.html")
    page.get_by_test_id("go-registro").click()
    expect(page).to_have_url(re.compile("/registro.html"))


def test_link_a_login(page):
    page.goto("/registro.html")
    page.get_by_test_id("go-login").click()
    expect(page).to_have_url(re.compile("/login.html"))


# ---------- Registro ----------
def fill_registro(page, nombre, email, password, confirmar=None):
    page.goto("/registro.html")
    page.get_by_test_id("nombre").fill(nombre)
    page.get_by_test_id("email").fill(email)
    page.get_by_test_id("password").fill(password)
    page.get_by_test_id("confirmar").fill(password if confirmar is None else confirmar)
    page.get_by_test_id("submit").click()


def test_registro_exitoso_redirige_al_login(page):
    fill_registro(page, "Nueva Persona", unique_email(), PASSWORD)
    expect(page).to_have_url(re.compile(r"/login.html\?registrado=1"))
    expect(page.get_by_test_id("alert")).to_contain_text("Cuenta creada")


def test_registro_passwords_distintas(page):
    fill_registro(page, "Ana", unique_email(), PASSWORD, "OtraClave1")
    expect(page.get_by_test_id("alert")).to_have_text("Las contraseñas no coinciden")


def test_registro_campos_vacios(page):
    page.goto("/registro.html")
    page.get_by_test_id("submit").click()
    expect(page.get_by_test_id("alert")).to_have_text("Todos los campos son obligatorios")


@pytest.mark.parametrize("email, password, message", [
    ("no-es-correo", PASSWORD, "El correo no tiene un formato válido"),
    ("ok@example.com", "corta1", "al menos 8 caracteres"),
    ("ok@example.com", "sinnumeros", "letras y números"),
])
def test_registro_validaciones_del_servidor(page, email, password, message):
    fill_registro(page, "Ana", email, password)
    expect(page.get_by_test_id("alert")).to_contain_text(message)


def test_registro_correo_existente(page, user):
    fill_registro(page, "Copia", user["email"], PASSWORD)
    expect(page.get_by_test_id("alert")).to_have_text("El correo ya está registrado")


# ---------- Login ----------
def test_login_campos_vacios(page):
    page.goto("/login.html")
    page.get_by_test_id("submit").click()
    expect(page.get_by_test_id("alert")).to_have_text("El correo y la contraseña son obligatorios")


def test_login_credenciales_incorrectas(page, user):
    page.goto("/login.html")
    page.get_by_test_id("email").fill(user["email"])
    page.get_by_test_id("password").fill("Incorrecta1")
    page.get_by_test_id("submit").click()
    expect(page.get_by_test_id("alert")).to_have_text("Correo o contraseña incorrectos")


def test_login_correcto_muestra_usuarios(logged_page, user):
    expect(logged_page.get_by_role("heading", name="Usuarios")).to_be_visible()
    expect(logged_page.get_by_test_id("current-user")).to_contain_text(user["email"])
    expect(logged_page.get_by_test_id(f"usuario-{user['id']}")).to_be_visible()


def test_login_con_sesion_redirige_a_usuarios(logged_page):
    logged_page.goto("/login.html")
    expect(logged_page).to_have_url(re.compile("/usuarios.html"))


def test_logout(logged_page):
    logged_page.get_by_test_id("logout").click()
    expect(logged_page).to_have_url(re.compile("/login.html"))
    logged_page.goto("/usuarios.html")
    expect(logged_page).to_have_url(re.compile("/login.html"))


def test_token_invalido_regresa_al_login(page):
    page.goto("/login.html")
    page.evaluate("""() => {
        localStorage.setItem('token', 'token.invalido');
        localStorage.setItem('usuario', JSON.stringify({id: 0, nombre: 'X', email: 'x@x.com'}));
    }""")
    page.goto("/usuarios.html")
    expect(page).to_have_url(re.compile(r"/login.html\?expirada=1"))


# ---------- CRUD ----------
def crear_desde_ui(page, nombre, email, password=PASSWORD):
    page.get_by_test_id("nuevo").click()
    page.get_by_test_id("f-nombre").fill(nombre)
    page.get_by_test_id("f-email").fill(email)
    page.get_by_test_id("f-password").fill(password)
    page.get_by_test_id("guardar").click()


def test_crear_usuario(logged_page):
    email = unique_email("ui")
    crear_desde_ui(logged_page, "Creado Desde UI", email)
    expect(logged_page.get_by_test_id("alert")).to_have_text("Usuario creado")
    expect(logged_page.get_by_test_id("tabla-usuarios")).to_contain_text(email)


def test_crear_usuario_sin_password(logged_page):
    logged_page.get_by_test_id("nuevo").click()
    logged_page.get_by_test_id("f-nombre").fill("Sin Clave")
    logged_page.get_by_test_id("f-email").fill(unique_email())
    logged_page.get_by_test_id("guardar").click()
    expect(logged_page.get_by_test_id("dialogo-alert")).to_have_text("La contraseña es obligatoria")


def test_crear_usuario_correo_duplicado(logged_page, user):
    crear_desde_ui(logged_page, "Duplicado", user["email"])
    expect(logged_page.get_by_test_id("dialogo-alert")).to_have_text("El correo ya está registrado")


def test_cancelar_dialogo(logged_page):
    logged_page.get_by_test_id("nuevo").click()
    expect(logged_page.get_by_test_id("dialogo")).to_be_visible()
    logged_page.get_by_test_id("cancelar").click()
    expect(logged_page.get_by_test_id("dialogo")).to_be_hidden()


def test_editar_usuario(logged_page):
    email = unique_email("edit")
    crear_desde_ui(logged_page, "Antes", email)
    fila = logged_page.get_by_role("row").filter(has_text=email)
    fila.get_by_test_id("editar").click()
    expect(logged_page.get_by_test_id("f-nombre")).to_have_value("Antes")
    logged_page.get_by_test_id("f-nombre").fill("Después")
    logged_page.get_by_test_id("guardar").click()
    expect(logged_page.get_by_test_id("alert")).to_have_text("Usuario actualizado")
    expect(fila).to_contain_text("Después")


def test_eliminar_usuario(logged_page):
    email = unique_email("del")
    crear_desde_ui(logged_page, "Para Borrar", email)
    fila = logged_page.get_by_role("row").filter(has_text=email)
    fila.get_by_test_id("eliminar").click()
    expect(logged_page.get_by_test_id("confirmar")).to_contain_text(email)
    logged_page.get_by_test_id("confirmar-si").click()
    expect(logged_page.get_by_test_id("alert")).to_have_text("Usuario eliminado")
    expect(fila).to_have_count(0)


def test_cancelar_eliminacion(logged_page):
    email = unique_email("keep")
    crear_desde_ui(logged_page, "Se Queda", email)
    fila = logged_page.get_by_role("row").filter(has_text=email)
    fila.get_by_test_id("eliminar").click()
    logged_page.get_by_test_id("confirmar-no").click()
    expect(fila).to_have_count(1)


def test_no_puede_eliminarse_a_si_mismo(logged_page, user):
    expect(logged_page.get_by_test_id(f"usuario-{user['id']}").get_by_test_id("eliminar")).to_be_disabled()


def test_buscar_filtra_la_tabla(logged_page, user):
    logged_page.get_by_test_id("buscar").fill(user["email"])
    expect(logged_page.locator("[data-testid^='usuario-']")).to_have_count(1)


def test_buscar_sin_resultados(logged_page):
    logged_page.get_by_test_id("buscar").fill("zzz-no-existe-zzz")
    expect(logged_page.get_by_test_id("vacio")).to_be_visible()
