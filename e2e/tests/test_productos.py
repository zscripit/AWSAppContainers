"""Frontend: CRUD de productos desde el navegador."""
import re
import uuid

import pytest
from playwright.sync_api import expect


@pytest.fixture
def productos(logged_page):
    logged_page.get_by_test_id("nav-productos").click()
    expect(logged_page).to_have_url(re.compile("/productos.html"))
    expect(logged_page.locator("[data-testid^='producto-']").first).to_be_visible()
    return logged_page


def nombre_unico(prefijo="Producto"):
    return f"{prefijo} {uuid.uuid4().hex[:8]}"


def crear_desde_ui(page, nombre, precio="49.90", categoria="Bebidas"):
    page.get_by_test_id("nuevo").click()
    page.get_by_test_id("f-nombre").fill(nombre)
    page.get_by_test_id("f-precio").fill(precio)
    if categoria:
        page.get_by_test_id("f-categoria").select_option(label=categoria)
    page.get_by_test_id("guardar").click()


def fila(page, nombre):
    return page.get_by_role("row").filter(has_text=nombre)


# ---------- Navegación ----------
def test_productos_sin_sesion_va_al_login(page):
    page.goto("/productos.html")
    expect(page).to_have_url(re.compile("/login.html"))


def test_menu_lleva_a_productos_y_regresa(productos):
    expect(productos.get_by_role("heading", name="Productos")).to_be_visible()
    expect(productos.get_by_test_id("nav-productos")).to_have_class(re.compile("active"))
    productos.get_by_test_id("nav-usuarios").click()
    expect(productos).to_have_url(re.compile("/usuarios.html"))


def test_productos_muestra_usuario_y_logout(productos, user):
    expect(productos.get_by_test_id("current-user")).to_contain_text(user["email"])
    productos.get_by_test_id("logout").click()
    expect(productos).to_have_url(re.compile("/login.html"))


# ---------- Listado y filtros ----------
@pytest.mark.parametrize("nombre", ["Café", "Té verde", "Galletas"])
def test_lista_productos_sembrados(productos, nombre):
    expect(fila(productos, nombre).first).to_be_visible()


def test_precio_con_formato_de_moneda(productos):
    expect(fila(productos, "Café").first).to_contain_text("$35.50")


def test_filtro_por_categoria(productos):
    productos.get_by_test_id("filtro-categoria").select_option(label="Snacks")
    expect(fila(productos, "Galletas").first).to_be_visible()
    expect(fila(productos, "Café")).to_have_count(0)


def test_buscar_por_nombre(productos):
    productos.get_by_test_id("buscar").fill("galle")
    expect(productos.locator("[data-testid^='producto-']")).to_have_count(1)
    expect(fila(productos, "Galletas")).to_have_count(1)


def test_buscar_sin_resultados(productos):
    productos.get_by_test_id("buscar").fill("zzz-no-existe-zzz")
    expect(productos.get_by_test_id("vacio")).to_be_visible()


# ---------- Crear ----------
@pytest.mark.parametrize("categoria", ["Bebidas", "Snacks"])
def test_crear_producto(productos, categoria):
    nombre = nombre_unico()
    crear_desde_ui(productos, nombre, "12.50", categoria)
    expect(productos.get_by_test_id("alert")).to_have_text("Producto creado")
    expect(fila(productos, nombre)).to_contain_text(categoria)
    expect(fila(productos, nombre)).to_contain_text("$12.50")


def test_crear_sin_nombre(productos):
    crear_desde_ui(productos, "", "10")
    expect(productos.get_by_test_id("dialogo-alert")).to_have_text("El nombre es obligatorio")


def test_crear_sin_precio(productos):
    crear_desde_ui(productos, nombre_unico(), "")
    expect(productos.get_by_test_id("dialogo-alert")).to_have_text("El precio es obligatorio")


def test_crear_sin_categoria(productos):
    crear_desde_ui(productos, nombre_unico(), "10", categoria=None)
    expect(productos.get_by_test_id("dialogo-alert")).to_have_text("Selecciona una categoría")


def test_crear_precio_negativo_lo_rechaza_el_servidor(productos):
    crear_desde_ui(productos, nombre_unico(), "-5")
    expect(productos.get_by_test_id("dialogo-alert")).to_have_text("El precio no puede ser negativo")


def test_cancelar_dialogo(productos):
    productos.get_by_test_id("nuevo").click()
    expect(productos.get_by_test_id("dialogo")).to_be_visible()
    productos.get_by_test_id("cancelar").click()
    expect(productos.get_by_test_id("dialogo")).to_be_hidden()


# ---------- Editar y eliminar ----------
def test_editar_producto(productos):
    nombre = nombre_unico("Editar")
    crear_desde_ui(productos, nombre, "20")
    fila(productos, nombre).get_by_test_id("editar").click()
    expect(productos.get_by_test_id("f-nombre")).to_have_value(nombre)
    expect(productos.get_by_test_id("f-precio")).to_have_value("20")
    productos.get_by_test_id("f-precio").fill("99.99")
    productos.get_by_test_id("f-categoria").select_option(label="Snacks")
    productos.get_by_test_id("guardar").click()
    expect(productos.get_by_test_id("alert")).to_have_text("Producto actualizado")
    expect(fila(productos, nombre)).to_contain_text("$99.99")
    expect(fila(productos, nombre)).to_contain_text("Snacks")


def test_eliminar_producto(productos):
    nombre = nombre_unico("Borrar")
    crear_desde_ui(productos, nombre)
    fila(productos, nombre).get_by_test_id("eliminar").click()
    expect(productos.get_by_test_id("confirmar")).to_contain_text(nombre)
    productos.get_by_test_id("confirmar-si").click()
    expect(productos.get_by_test_id("alert")).to_have_text("Producto eliminado")
    expect(fila(productos, nombre)).to_have_count(0)


def test_cancelar_eliminacion(productos):
    nombre = nombre_unico("Queda")
    crear_desde_ui(productos, nombre)
    fila(productos, nombre).get_by_test_id("eliminar").click()
    productos.get_by_test_id("confirmar-no").click()
    expect(fila(productos, nombre)).to_have_count(1)


def test_producto_creado_aparece_en_la_api(productos, api):
    nombre = nombre_unico("Api")
    crear_desde_ui(productos, nombre, "7.25", "Snacks")
    expect(productos.get_by_test_id("alert")).to_have_text("Producto creado")
    nombres = [p["nombre"] for p in api.get("/api/productos").json()["data"]]
    assert nombre in nombres
