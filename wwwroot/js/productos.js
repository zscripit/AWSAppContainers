import { api, showAlert, hideAlert } from "/js/api.js";
import "/js/layout.js";

const $ = (id) => document.getElementById(id);
const alert = $("alert");
const filas = $("filas");
const dialogo = $("dialogo");
const form = $("producto-form");
const dialogoAlert = $("dialogo-alert");
const confirmar = $("confirmar");
const moneda = new Intl.NumberFormat("es-MX", { style: "currency", currency: "MXN" });

let productos = [];
let editandoId = null;
let eliminandoId = null;

function opcion(value, text) {
  const o = document.createElement("option");
  o.value = value;
  o.textContent = text;
  return o;
}

async function cargarCategorias() {
  const categorias = await api("/api/categorias");
  $("filtro-categoria").replaceChildren(opcion("", "Todas las categorías"), ...categorias.map((c) => opcion(c.id, c.nombre)));
  $("f-categoria").replaceChildren(opcion("", "Selecciona una categoría"), ...categorias.map((c) => opcion(c.id, c.nombre)));
}

function fila(p) {
  const tr = document.createElement("tr");
  tr.setAttribute("data-testid", `producto-${p.id}`);
  const celdas = [[p.id], [p.nombre], [moneda.format(p.precio), "numero"], [p.categoria]];
  for (const [valor, clase] of celdas) {
    const td = document.createElement("td");
    td.textContent = valor;
    if (clase) td.className = clase;
    tr.append(td);
  }
  const acciones = document.createElement("td");
  acciones.className = "actions";

  const editar = document.createElement("button");
  editar.className = "secondary";
  editar.textContent = "Editar";
  editar.setAttribute("data-testid", "editar");
  editar.addEventListener("click", () => abrirFormulario(p));

  const eliminar = document.createElement("button");
  eliminar.className = "danger";
  eliminar.textContent = "Eliminar";
  eliminar.setAttribute("data-testid", "eliminar");
  eliminar.addEventListener("click", () => pedirConfirmacion(p));

  acciones.append(editar, eliminar);
  tr.append(acciones);
  return tr;
}

// La búsqueda por nombre se hace en el navegador; el filtro por categoría lo resuelve la API.
function pintar() {
  const texto = $("buscar").value.trim().toLowerCase();
  const visibles = productos.filter((p) => p.nombre.toLowerCase().includes(texto));
  filas.replaceChildren(...visibles.map(fila));
  $("vacio").hidden = visibles.length > 0;
}

async function cargar() {
  const categoriaId = $("filtro-categoria").value;
  try {
    productos = await api(`/api/productos${categoriaId ? `?categoriaId=${categoriaId}` : ""}`);
    pintar();
  } catch (err) {
    showAlert(alert, err.message);
  }
}

function abrirFormulario(p = null) {
  editandoId = p?.id ?? null;
  hideAlert(dialogoAlert);
  form.reset();
  $("dialogo-titulo").textContent = p ? "Editar producto" : "Nuevo producto";
  if (p) {
    form.nombre.value = p.nombre;
    form.precio.value = p.precio;
    form.categoria.value = p.categoriaId;
  }
  dialogo.showModal();
  form.nombre.focus();
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  hideAlert(dialogoAlert);
  const nombre = form.nombre.value.trim();
  const precio = form.precio.value === "" ? NaN : Number(form.precio.value);
  const categoriaId = Number(form.categoria.value);
  if (!nombre) return showAlert(dialogoAlert, "El nombre es obligatorio");
  if (Number.isNaN(precio)) return showAlert(dialogoAlert, "El precio es obligatorio");
  if (!categoriaId) return showAlert(dialogoAlert, "Selecciona una categoría");

  $("guardar").disabled = true;
  try {
    const body = { nombre, precio, categoriaId };
    if (editandoId) {
      await api(`/api/productos/${editandoId}`, { method: "PUT", body });
      showAlert(alert, "Producto actualizado", "success");
    } else {
      await api("/api/productos", { method: "POST", body });
      showAlert(alert, "Producto creado", "success");
    }
    dialogo.close();
    await cargar();
  } catch (err) {
    showAlert(dialogoAlert, err.message);
  } finally {
    $("guardar").disabled = false;
  }
});

$("cancelar").addEventListener("click", () => dialogo.close());
$("nuevo").addEventListener("click", () => abrirFormulario());

function pedirConfirmacion(p) {
  eliminandoId = p.id;
  $("confirmar-texto").textContent = `¿Eliminar "${p.nombre}"? Esta acción no se puede deshacer.`;
  confirmar.showModal();
}

$("confirmar-no").addEventListener("click", () => confirmar.close());
$("confirmar-si").addEventListener("click", async () => {
  try {
    await api(`/api/productos/${eliminandoId}`, { method: "DELETE" });
    showAlert(alert, "Producto eliminado", "success");
    await cargar();
  } catch (err) {
    showAlert(alert, err.message);
  } finally {
    confirmar.close();
  }
});

$("buscar").addEventListener("input", pintar);
$("filtro-categoria").addEventListener("change", cargar);

try {
  await cargarCategorias();
} catch (err) {
  showAlert(alert, err.message);
}
cargar();
