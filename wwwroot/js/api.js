// Cliente de la API y manejo de la sesión (token JWT en localStorage).
const TOKEN_KEY = "token";
const USER_KEY = "usuario";

export const session = {
  get token() { return localStorage.getItem(TOKEN_KEY); },
  get usuario() {
    try { return JSON.parse(localStorage.getItem(USER_KEY)); } catch { return null; }
  },
  save(token, usuario) {
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(USER_KEY, JSON.stringify(usuario));
  },
  clear() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
  },
};

// Todas las respuestas de la API tienen el schema { statusCode, data: [] }.
export async function api(path, { method = "GET", body } = {}) {
  const headers = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (session.token) headers.Authorization = `Bearer ${session.token}`;

  const resp = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const json = await resp.json().catch(() => ({ statusCode: resp.status, data: [{ error: "Respuesta inválida del servidor" }] }));

  if (resp.status === 401 && path !== "/api/auth/login") {
    session.clear();
    window.location.href = "/login.html?expirada=1";
    throw new Error("Sesión expirada");
  }
  if (!resp.ok) throw new Error(json.data?.[0]?.error ?? "Ocurrió un error");
  return json.data;
}

export function showAlert(el, message, type = "error") {
  el.textContent = message;
  el.className = `alert ${type}`;
  el.hidden = false;
}

export function hideAlert(el) {
  el.hidden = true;
}
