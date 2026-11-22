import { api, session, showAlert, hideAlert } from "/js/api.js";
import "/js/layout.js";

const $ = (id) => document.getElementById(id);
const alert = $("alert");
const filas = $("filas");
const dialogo = $("dialogo");
const form = $("usuario-form");
const dialogoAlert = $("dialogo-alert");
const confirmar = $("confirmar");

let editandoId = null;
let eliminandoId = null;
let busquedaTimer;

function fila(u) {
  const tr = document.createElement("tr");
  tr.setAttribute("data-testid", `usuario-${u.id}`);
  const celdas = [u.id, u.nombre, u.email, new Date(u.creadoEn).toLocaleString()];
  for (const valor of celdas) {
    const td = document.createElement("td");
    td.textContent = valor;
    tr.append(td);
  }
  const acciones = document.createElement("td");
  acciones.className = "actions";

  const editar = document.createElement("button");
  editar.className = "secondary";
  editar.textContent = "Editar";
  editar.setAttribute("data-testid", "editar");
  editar.addEventListener("click", () => abrirFormulario(u));

  const eliminar = document.createElement("button");
  eliminar.className = "danger";
  eliminar.textContent = "Eliminar";
  eliminar.setAttribute("data-testid", "eliminar");
  eliminar.disabled = session.usuario?.id === u.id;
  eliminar.title = eliminar.disabled ? "No puedes eliminar tu propia cuenta" : "";
  eliminar.addEventListener("click", () => pedirConfirmacion(u));

  acciones.append(editar, eliminar);
  tr.append(acciones);
  return tr;
}

async function cargar() {
  const buscar = $("buscar").value.trim();
  try {
    const usuarios = await api(`/api/usuarios${buscar ? `?buscar=${encodeURIComponent(buscar)}` : ""}`);
    filas.replaceChildren(...usuarios.map(fila));
    $("vacio").hidden = usuarios.length > 0;
  } catch (err) {
    showAlert(alert, err.message);
  }
}

function abrirFormulario(u = null) {
  editandoId = u?.id ?? null;
  hideAlert(dialogoAlert);
  form.reset();
  $("dialogo-titulo").textContent = u ? "Editar usuario" : "Nuevo usuario";
  $("password-hint").textContent = u
    ? "Déjala vacía para conservar la contraseña actual."
    : "Mínimo 8 caracteres, con letras y números.";
  if (u) {
    form.nombre.value = u.nombre;
    form.email.value = u.email;
  }
  dialogo.showModal();
  form.nombre.focus();
}

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  hideAlert(dialogoAlert);
  const body = { nombre: form.nombre.value.trim(), email: form.email.value.trim(), password: form.password.value };
  if (!body.nombre || !body.email) return showAlert(dialogoAlert, "El nombre y el correo son obligatorios");
  if (!editandoId && !body.password) return showAlert(dialogoAlert, "La contraseña es obligatoria");

  $("guardar").disabled = true;
  try {
    if (editandoId) {
      await api(`/api/usuarios/${editandoId}`, { method: "PUT", body });
      showAlert(alert, "Usuario actualizado", "success");
    } else {
      await api("/api/usuarios", { method: "POST", body });
      showAlert(alert, "Usuario creado", "success");
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

function pedirConfirmacion(u) {
  eliminandoId = u.id;
  $("confirmar-texto").textContent = `¿Eliminar a ${u.nombre} (${u.email})? Esta acción no se puede deshacer.`;
  confirmar.showModal();
}

$("confirmar-no").addEventListener("click", () => confirmar.close());
$("confirmar-si").addEventListener("click", async () => {
  try {
    await api(`/api/usuarios/${eliminandoId}`, { method: "DELETE" });
    showAlert(alert, "Usuario eliminado", "success");
    await cargar();
  } catch (err) {
    showAlert(alert, err.message);
  } finally {
    confirmar.close();
  }
});

$("buscar").addEventListener("input", () => {
  clearTimeout(busquedaTimer);
  busquedaTimer = setTimeout(cargar, 250);
});

cargar();
