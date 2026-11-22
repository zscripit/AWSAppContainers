// Barra superior compartida por las páginas con sesión: usuario actual y cierre de sesión.
import { session } from "/js/api.js";

const actual = document.getElementById("current-user");
if (actual && session.usuario) actual.textContent = `${session.usuario.nombre} (${session.usuario.email})`;

document.getElementById("logout")?.addEventListener("click", () => {
  session.clear();
  window.location.href = "/login.html";
});
