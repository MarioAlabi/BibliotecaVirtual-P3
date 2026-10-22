"use strict";

function getDefaultApi() {
  const meta = document.querySelector('meta[name="api-url"]')?.content;
  if (meta) return meta.replace(/\/$/, "");
  const host = window.location.hostname;
  if (host.includes("biblioteca.marioalabi.com")) {
    return "https://biblioteca-api.marioalabi.com";
  }
  if (window.location.port === "5080") {
    return `${window.location.protocol}//${host}:5081`;
  }
  if (host === "localhost" || host === "127.0.0.1") {
    return `http://${host}:5081`;
  }
  return "http://localhost:5081";
}

const DEFAULT_API = getDefaultApi();
let API = localStorage.getItem("biblioteca_api_url") || DEFAULT_API;
let token = localStorage.getItem("biblioteca_jwt") || "";
let user = JSON.parse(localStorage.getItem("biblioteca_user") || "null");

const main = document.querySelector("#main");
const modal = document.querySelector("#modal");
let page = 1;
let currentSearch = "";
let currentAuthor = "";
let currentCategory = "";
let currentAvailable = "";
let currentLoanTab = "activos";
let viewRevision = 0;

// Role helpers
const isAdmin = () => user?.roles?.includes("Administrador");
const isLibrarian = () => user?.roles?.includes("Bibliotecario");
const isReader = () => user?.roles?.includes("Lector");
const canManageCatalog = () => isAdmin() || isLibrarian();
const canManageLoans = () => isAdmin() || isLibrarian();

const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) => ({
  "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
}[c]));

const date = (v) => {
  if (!v) return "—";
  const s = String(v);
  return new Date(s.endsWith("Z") ? s : s + "Z").toLocaleString("es-SV", {
    dateStyle: "medium", timeStyle: "short"
  });
};

const dateOnly = (v) => {
  if (!v) return "—";
  return new Date(v + "T00:00:00").toLocaleDateString("es-SV", { dateStyle: "medium" });
};

// API Fetch client with JWT Bearer
async function api(path, method = "GET", body = null, isFormData = false) {
  const headers = {};
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }
  if (body && !isFormData) {
    headers["Content-Type"] = "application/json";
  }

  const options = {
    method,
    headers,
    ...(body ? { body: isFormData ? body : JSON.stringify(body) } : {})
  };

  const response = await fetch(API + path, options);
  const data = await response.json().catch(() => null);

  if (!response.ok) {
    if (response.status === 401 && token) {
      logout(false);
      throw new Error("Tu sesión ha expirado. Por favor, inicia sesión de nuevo.");
    }

    let msg = data?.message
      || (data?.errors ? Object.values(data.errors).flat().join(" ") : null)
      || ({
        400: "Los datos enviados son inválidos. Revisa el formulario.",
        401: "Inicia sesión para continuar.",
        403: "No tienes permisos para realizar esta acción.",
        404: "El recurso solicitado no fue encontrado.",
        409: "Conflicto: los datos cambiaron en otra sesión o violan una restricción del sistema.",
        429: "Demasiadas solicitudes. Espera un momento antes de reintentar."
      }[response.status])
      || "Ocurrió un error inesperado.";

    const err = new Error(msg);
    err.status = response.status;
    err.data = data;
    throw err;
  }

  return data;
}

function notify(message, error = false) {
  const n = document.querySelector("#notice");
  n.textContent = message;
  n.className = "notice" + (error ? " error" : "");
  n.hidden = false;
  clearTimeout(notify.timer);
  notify.timer = setTimeout(() => { n.hidden = true; }, 5000);
}

function updateNavigation() {
  document.querySelector("[data-nav=usuarios]").hidden = !isAdmin();
  document.querySelector("[data-nav=lectores]").hidden = !canManageLoans();
  document.querySelector("[data-nav=prestamos]").hidden = !user;
  document.querySelector("[data-nav=cuenta]").hidden = !user;

  const authActions = document.querySelector("#auth-actions");
  if (user) {
    authActions.innerHTML = `
      <a class="button secondary small" href="#cuenta">
        <strong>${esc(user.nombre || user.email)}</strong> <span class="pill">${esc(user.roles?.[0] || "Usuario")}</span>
      </a>
    `;
  } else {
    authActions.innerHTML = `<a class="button secondary small" href="#login">Iniciar sesión <span>↗</span></a>`;
  }
}

async function checkApiStatus() {
  const badge = document.querySelector("#api-status-badge");
  try {
    const res = await fetch(API + "/health/live", { method: "GET" });
    if (res.ok) {
      badge.textContent = `API: En línea (${API.replace(/^https?:\/\//, "")})`;
      badge.style.borderColor = "#a7f3d0";
      badge.style.color = "#065f46";
      badge.style.background = "#ecfdf5";
    } else {
      throw new Error();
    }
  } catch {
    badge.textContent = "API: Desconectada";
    badge.style.borderColor = "#fecaca";
    badge.style.color = "#991b1b";
    badge.style.background = "#fef2f2";
  }
}

function heading(title, subtitle = "", action = "") {
  return `
    <div class="page-head">
      <div>
        <div class="eyebrow">BIBLIOTECA VIRTUAL · P3</div>
        <h1>${title}</h1>
        ${subtitle ? `<p>${subtitle}</p>` : ""}
      </div>
      <div>${action}</div>
    </div>
  `;
}

function pagination(total, size, onPageChange) {
  const totalPages = Math.max(1, Math.ceil(total / size));
  return `
    <div class="pagination">
      <button class="button secondary small" data-page="${page - 1}" ${page <= 1 ? "disabled" : ""}>← Anterior</button>
      <span>Página ${page} de ${totalPages} · ${total} registros</span>
      <button class="button secondary small" data-page="${page + 1}" ${page >= totalPages ? "disabled" : ""}>Siguiente →</button>
    </div>
  `;
}

function bindPaging(renderFn) {
  main.querySelectorAll("[data-page]").forEach((b) => {
    b.onclick = () => {
      page = +b.dataset.page;
      renderFn().catch(showError);
    };
  });
}

function showError(e) {
  main.innerHTML = `
    <div class="panel">
      <h2>Error al cargar el contenido</h2>
      <p class="error">${esc(e.message === "Failed to fetch" ? "No se pudo conectar con la API REST (" + API + "). Comprueba que el servidor esté activo." : e.message)}</p>
      <div style="display:flex;gap:10px;">
        <button class="button primary" id="retry">Reintentar</button>
        <button class="button secondary" id="config-api-btn">Cambiar URL de API</button>
      </div>
    </div>
  `;
  document.querySelector("#retry").onclick = route;
  document.querySelector("#config-api-btn").onclick = openApiConfigModal;
}

function openModal(title, html) {
  document.querySelector("#modal-title").textContent = title;
  document.querySelector("#modal-content").innerHTML = html;
  if (!modal.open) modal.showModal();
}

document.querySelector("#close-modal").onclick = () => modal.close();

function formError(form, e, reloadCallback) {
  let box = form.querySelector(".error");
  if (!box) {
    box = document.createElement("div");
    box.className = "error";
    box.setAttribute("role", "alert");
    form.prepend(box);
  }
  box.textContent = e.message;
  if (e.status === 409 && reloadCallback) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "button secondary small";
    btn.style.marginTop = "8px";
    btn.textContent = "Recargar datos actuales";
    btn.onclick = async () => {
      try {
        await reloadCallback();
        notify("Datos recargados.");
      } catch (err) {
        notify(err.message, true);
      }
    };
    box.appendChild(document.createElement("br"));
    box.appendChild(btn);
  }
}

function submitForm(form, action, reloadCallback) {
  form.onsubmit = async (e) => {
    e.preventDefault();
    if (!form.reportValidity()) return;
    const btn = form.querySelector("[type=submit]");
    if (btn) btn.disabled = true;
    try {
      await action(new FormData(form));
    } catch (err) {
      formError(form, err, reloadCallback);
    } finally {
      if (btn) btn.disabled = false;
    }
  };
}

function card(b, idx = 0) {
  const coverHtml = b.portadaUrl
    ? `<img src="${API + b.portadaUrl}" alt="${esc(b.titulo)}" class="cover-img" onerror="this.onerror=null;this.parentElement.innerHTML='<div class=\'cover\'><span>${esc(b.titulo)}</span><small>${esc(b.autorNombre)}</small></div>';">`
    : `<div class="cover"><span>${esc(b.titulo)}</span><small>${esc(b.autorNombre)}</small></div>`;

  return `
    <a class="book-card tone-${idx % 4}" href="#libro/${b.id}">
      <div class="cover-area">${coverHtml}</div>
      <div class="card-body">
        <span class="genre">${esc(b.categoriaNombre)} · ${b.anioPublicacion}</span>
        <h3>${esc(b.titulo)}</h3>
        <p>${esc(b.autorNombre)}</p>
        <div class="card-bottom">
          <span class="badge ${b.disponible ? "" : "busy"}">${b.disponible ? "● Disponible" : "● Prestado"}</span>
          <span aria-hidden="true">↗</span>
        </div>
      </div>
    </a>
  `;
}

// ==================== VISTA: INICIO ====================
async function home() {
  const revision = ++viewRevision;
  main.innerHTML = `
    <section class="hero">
      <div class="hero-copy">
        <div class="eyebrow">ARQUITECTURA RESTFUL · .NET 10 & MARIADB</div>
        <h1>Gestión de Biblioteca<br><em>Centralizada y Concurrente</em></h1>
        <p>Servicios REST desacoplados para administrar libros, autores, categorías, lectores y préstamos con autenticación JWT.</p>
        <div style="display:flex;gap:12px;">
          <a class="button primary" href="#catalogo">Explorar catálogo <span>↗</span></a>
          <a class="button secondary" href="${API}/swagger" target="_blank">Documentación Swagger ↗</a>
        </div>
      </div>
      <div class="book-art" aria-hidden="true">
        <div class="art-orbit"></div>
        <div class="art-star">✳</div>
        <div class="drawn-book book-a">
          <span>SISTEMA<br>RESTFUL</span>
          <small>PERIODO 3</small>
        </div>
        <div class="drawn-book book-b">
          <span>RowVersion<br>& JWT</span>
          <small>MARIADB</small>
        </div>
        <div class="art-caption">DESARROLLO MULTIPLATAFORMA · 2026</div>
      </div>
    </section>

    <section class="stats" aria-label="Resumen del sistema">
      <div>
        <span>01 / CATÁLOGO</span>
        <strong id="stat-books">—</strong>
        <p>Libros registrados</p>
      </div>
      <div>
        <span>02 / DISPONIBLES</span>
        <strong id="stat-available">—</strong>
        <p>Listos para préstamo</p>
      </div>
      <div>
        <span>03 / EN PRÉSTAMO</span>
        <strong id="stat-loans">—</strong>
        <p>Préstamos activos</p>
      </div>
    </section>

    <section>
      <div class="section-heading">
        <div>
          <div class="eyebrow">NOVEDADES</div>
          <h2>Títulos destacados</h2>
        </div>
        <a class="text-link" href="#catalogo">Ver todos los libros ↗</a>
      </div>
      <div id="home-books" class="book-grid">
        <div class="empty">Cargando títulos…</div>
      </div>
    </section>
  `;

  try {
    const [booksRes, availRes, loansRes] = await Promise.all([
      api("/api/libros?pageSize=4"),
      api("/api/libros/disponibles?pageSize=1"),
      api("/api/prestamos/activos?pageSize=1").catch(() => ({ total: "—" }))
    ]);

    if (revision !== viewRevision) return;

    document.querySelector("#stat-books").textContent = booksRes.total;
    document.querySelector("#stat-available").textContent = availRes.total;
    document.querySelector("#stat-loans").textContent = loansRes.total;

    const booksContainer = document.querySelector("#home-books");
    if (booksContainer) {
      booksContainer.innerHTML = booksRes.items.length
        ? booksRes.items.map(card).join("")
        : '<div class="empty">No hay libros registrados aún.</div>';
    }
  } catch (err) {
    if (revision === viewRevision) showError(err);
  }
}

// ==================== VISTA: CATÁLOGO ====================
async function catalog() {
  const revision = ++viewRevision;

  const [authorsRes, categoriesRes] = await Promise.all([
    api("/api/autores?pageSize=100").catch(() => ({ items: [] })),
    api("/api/categorias?pageSize=100").catch(() => ({ items: [] }))
  ]);

  const queryParams = new URLSearchParams({
    page,
    pageSize: 12,
    ...(currentSearch ? { search: currentSearch } : {}),
    ...(currentAuthor ? { autorId: currentAuthor } : {}),
    ...(currentCategory ? { categoriaId: currentCategory } : {}),
    ...(currentAvailable !== "" ? { disponible: currentAvailable } : {})
  });

  const res = await api(`/api/libros?${queryParams.toString()}`);
  if (revision !== viewRevision) return;

  const actionBtn = canManageCatalog()
    ? `<button class="button primary" id="new-book">+ Nuevo libro</button>`
    : "";

  main.innerHTML = heading("Catálogo de libros", "Explora por título, autor, categoría o disponibilidad.", actionBtn) + `
    <form class="toolbar" id="catalog-filters">
      <input name="search" placeholder="Buscar por título, autor o ISBN…" value="${esc(currentSearch)}">
      <select name="autorId">
        <option value="">Todos los autores</option>
        ${authorsRes.items.map((a) => `<option value="${a.id}" ${currentAuthor == a.id ? "selected" : ""}>${esc(a.nombre)}</option>`).join("")}
      </select>
      <select name="categoriaId">
        <option value="">Todas las categorías</option>
        ${categoriesRes.items.map((c) => `<option value="${c.id}" ${currentCategory == c.id ? "selected" : ""}>${esc(c.nombre)}</option>`).join("")}
      </select>
      <select name="disponible">
        <option value="">Disponibilidad</option>
        <option value="true" ${currentAvailable === "true" ? "selected" : ""}>Solo disponibles</option>
        <option value="false" ${currentAvailable === "false" ? "selected" : ""}>En préstamo</option>
      </select>
      <button class="button secondary" type="submit">Filtrar</button>
      <button class="button secondary" type="button" id="clear-filters">Limpiar</button>
    </form>
    <div class="book-grid">
      ${res.items.length ? res.items.map(card).join("") : '<div class="empty"><strong>Sin resultados</strong><p>Intenta con otros términos de búsqueda.</p></div>'}
    </div>
    ${pagination(res.total, 12, catalog)}
  `;

  document.querySelector("#catalog-filters").onsubmit = (e) => {
    e.preventDefault();
    const f = new FormData(e.target);
    currentSearch = f.get("search")?.trim() || "";
    currentAuthor = f.get("autorId") || "";
    currentCategory = f.get("categoriaId") || "";
    currentAvailable = f.get("disponible") || "";
    page = 1;
    catalog().catch(showError);
  };

  document.querySelector("#clear-filters").onclick = () => {
    currentSearch = "";
    currentAuthor = "";
    currentCategory = "";
    currentAvailable = "";
    page = 1;
    catalog().catch(showError);
  };

  if (canManageCatalog()) {
    document.querySelector("#new-book").onclick = () => bookModal();
  }

  bindPaging(catalog);
}

// ==================== VISTA: DETALLE DE LIBRO ====================
async function detail(id) {
  const revision = ++viewRevision;
  const b = await api(`/api/libros/${id}`);
  if (revision !== viewRevision) return;

  const coverHtml = b.portadaUrl
    ? `<img src="${API + b.portadaUrl}" alt="${esc(b.titulo)}" class="cover-img">`
    : `<div class="cover"><span>${esc(b.titulo)}</span><small>${esc(b.autorNombre)}</small></div>`;

  main.innerHTML = heading("Detalles del libro", '<a class="text-link" href="#catalogo">← Volver al catálogo</a>') + `
    <section class="panel detail-grid">
      <div class="cover-area">${coverHtml}</div>
      <div>
        <span class="genre">${esc(b.categoriaNombre)} · Publicado en ${b.anioPublicacion}</span>
        <h1>${esc(b.titulo)}</h1>
        <p class="muted">Autor: <strong>${esc(b.autorNombre)}</strong> · ISBN: ${esc(b.isbn || "No asignado")}</p>
        <span class="badge ${b.disponible ? "" : "busy"}">${b.disponible ? "● Disponible para préstamo" : "● Actualmente prestado"}</span>
        <p style="font-size:11px;color:var(--muted);margin:8px 0;">Versión de concurrencia (RowVersion): <code>${b.rowVersion}</code></p>

        <div class="form-actions" style="margin-top:25px;">
          ${b.disponible && canManageLoans() ? `<button class="button primary" id="btn-prestar">Registrar préstamo</button>` : ""}
          ${canManageCatalog() ? `
            <button class="button secondary" id="btn-editar">Editar libro</button>
            <button class="button secondary" id="btn-portada">Subir portada</button>
            <button class="button danger" id="btn-eliminar">Eliminar libro</button>
          ` : ""}
          ${!user ? `<a class="button primary" href="#login">Inicia sesión para gestionar</a>` : ""}
        </div>
      </div>
    </section>
  `;

  if (canManageCatalog()) {
    document.querySelector("#btn-editar").onclick = () => bookModal(b);
    document.querySelector("#btn-portada").onclick = () => coverModal(b);
    document.querySelector("#btn-eliminar").onclick = () => deleteBookModal(b);
  }

  const prestarBtn = document.querySelector("#btn-prestar");
  if (prestarBtn) {
    prestarBtn.onclick = () => loanModal(b);
  }
}

// Modal: Crear / Editar Libro
async function bookModal(b = null) {
  const [authors, categories] = await Promise.all([
    api("/api/autores?pageSize=100"),
    api("/api/categorias?pageSize=100")
  ]);

  openModal(b ? "Editar libro" : "Nuevo libro", `
    <form id="book-form">
      <label>Título del libro *
        <input name="titulo" required maxlength="200" value="${esc(b?.titulo)}">
      </label>
      <label>Autor *
        <select name="autorId" required>
          <option value="">Selecciona un autor</option>
          ${authors.items.map((a) => `<option value="${a.id}" ${b?.autorId === a.id ? "selected" : ""}>${esc(a.nombre)}</option>`).join("")}
        </select>
      </label>
      <label>Categoría *
        <select name="categoriaId" required>
          <option value="">Selecciona una categoría</option>
          ${categories.items.map((c) => `<option value="${c.id}" ${b?.categoriaId === c.id ? "selected" : ""}>${esc(c.nombre)}</option>`).join("")}
        </select>
      </label>
      <label>Año de publicación *
        <input name="anioPublicacion" type="number" min="1" max="2100" required value="${b?.anioPublicacion || new Date().getFullYear()}">
      </label>
      <label>ISBN (opcional)
        <input name="isbn" maxlength="30" value="${esc(b?.isbn)}">
      </label>
      <div class="form-actions">
        <button type="submit" class="button primary">Guardar libro</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#book-form"), async (f) => {
    const data = {
      titulo: f.get("titulo")?.trim(),
      autorId: +f.get("autorId"),
      categoriaId: +f.get("categoriaId"),
      anioPublicacion: +f.get("anioPublicacion"),
      isbn: f.get("isbn")?.trim() || null
    };

    if (b) {
      data.rowVersion = b.rowVersion;
      await api(`/api/libros/${b.id}`, "PUT", data);
      notify("Libro actualizado correctamente.");
    } else {
      await api("/api/libros", "POST", data);
      notify("Libro registrado en el catálogo.");
    }
    modal.close();
    route();
  }, async () => {
    if (b) {
      const latest = await api(`/api/libros/${b.id}`);
      bookModal(latest);
    }
  });
}

// Modal: Subir imagen de portada
function coverModal(b) {
  openModal(`Portada: ${esc(b.titulo)}`, `
    <form id="cover-form">
      <p>Selecciona un archivo de imagen (JPG, PNG o WebP, máx 5MB) para la portada:</p>
      <input type="file" name="imagen" accept="image/jpeg,image/png,image/webp" required>
      <div class="form-actions" style="margin-top:20px;">
        ${b.portadaUrl ? `<button type="button" class="button danger" id="btn-del-cover">Quitar portada actual</button>` : ""}
        <button type="submit" class="button primary">Subir portada</button>
      </div>
    </form>
  `);

  const delBtn = document.querySelector("#btn-del-cover");
  if (delBtn) {
    delBtn.onclick = async () => {
      try {
        await api(`/api/libros/${b.id}/imagen`, "DELETE");
        notify("Portada eliminada.");
        modal.close();
        detail(b.id);
      } catch (err) {
        notify(err.message, true);
      }
    };
  }

  submitForm(document.querySelector("#cover-form"), async (f) => {
    await api(`/api/libros/${b.id}/imagen`, "POST", f, true);
    notify("Portada actualizada exitosamente.");
    modal.close();
    detail(b.id);
  });
}

// Modal: Eliminar libro
function deleteBookModal(b) {
  openModal("Eliminar libro", `
    <form id="delete-book-form">
      <p>¿Estás seguro de eliminar <strong>${esc(b.titulo)}</strong>?</p>
      <p class="help">Solo se permite eliminar libros que no tengan historial de préstamos asociados.</p>
      <div class="form-actions">
        <button type="submit" class="button danger">Confirmar eliminación</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#delete-book-form"), async () => {
    await api(`/api/libros/${b.id}`, "DELETE", { rowVersion: b.rowVersion });
    notify("Libro eliminado del catálogo.");
    modal.close();
    location.hash = "catalogo";
  });
}

// ==================== VISTA: AUTORES ====================
async function authorsView() {
  const revision = ++viewRevision;
  const res = await api(`/api/autores?page=${page}&pageSize=20`);
  if (revision !== viewRevision) return;

  const actionBtn = canManageCatalog()
    ? `<button class="button primary" id="new-author">+ Nuevo autor</button>`
    : "";

  main.innerHTML = heading("Gestión de Autores", "Autores con obras registradas en el catálogo.", actionBtn) + `
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>AUTOR</th>
            <th>NACIONALIDAD</th>
            <th>NACIMIENTO</th>
            <th>LIBROS</th>
            <th>ACCIONES</th>
          </tr>
        </thead>
        <tbody>
          ${res.items.map((a) => `
            <tr>
              <td><strong>${esc(a.nombre)}</strong></td>
              <td>${esc(a.nacionalidad || "No especificada")}</td>
              <td>${dateOnly(a.fechaNacimiento)}</td>
              <td><span class="pill">${a.totalLibros} libros</span></td>
              <td>
                <div class="card-actions">
                  <button class="button secondary small" data-author-books="${a.id}">Ver libros</button>
                  ${canManageCatalog() ? `
                    <button class="button secondary small" data-edit-author="${a.id}">Editar</button>
                    <button class="button danger small" data-del-author="${a.id}">Eliminar</button>
                  ` : ""}
                </div>
              </td>
            </tr>
          `).join("")}
        </tbody>
      </table>
    </div>
    ${pagination(res.total, 20, authorsView)}
  `;

  if (canManageCatalog()) {
    document.querySelector("#new-author").onclick = () => authorModal();
  }

  main.querySelectorAll("[data-author-books]").forEach((btn) => {
    btn.onclick = () => {
      currentAuthor = btn.dataset.authorBooks;
      location.hash = "catalogo";
    };
  });

  if (canManageCatalog()) {
    main.querySelectorAll("[data-edit-author]").forEach((btn) => {
      btn.onclick = () => {
        const item = res.items.find((x) => x.id === +btn.dataset.editAuthor);
        authorModal(item);
      };
    });
    main.querySelectorAll("[data-del-author]").forEach((btn) => {
      btn.onclick = () => {
        const item = res.items.find((x) => x.id === +btn.dataset.delAuthor);
        deleteAuthorModal(item);
      };
    });
  }

  bindPaging(authorsView);
}

function authorModal(a = null) {
  openModal(a ? "Editar autor" : "Nuevo autor", `
    <form id="author-form">
      <label>Nombre completo *
        <input name="nombre" required maxlength="150" value="${esc(a?.nombre)}">
      </label>
      <label>Nacionalidad
        <input name="nacionalidad" maxlength="100" value="${esc(a?.nacionalidad)}">
      </label>
      <label>Fecha de nacimiento
        <input name="fechaNacimiento" type="date" value="${a?.fechaNacimiento || ""}">
      </label>
      <div class="form-actions">
        <button type="submit" class="button primary">Guardar autor</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#author-form"), async (f) => {
    const data = {
      nombre: f.get("nombre")?.trim(),
      nacionalidad: f.get("nacionalidad")?.trim() || null,
      fechaNacimiento: f.get("fechaNacimiento") || null
    };

    if (a) {
      await api(`/api/autores/${a.id}`, "PUT", data);
      notify("Autor actualizado.");
    } else {
      await api("/api/autores", "POST", data);
      notify("Autor registrado.");
    }
    modal.close();
    authorsView();
  });
}

function deleteAuthorModal(a) {
  openModal("Eliminar autor", `
    <form id="delete-author-form">
      <p>¿Deseas eliminar al autor <strong>${esc(a.nombre)}</strong>?</p>
      <p class="help">Si el autor tiene libros asociados en el catálogo, la API rechazará la operación con código 409.</p>
      <div class="form-actions">
        <button type="submit" class="button danger">Confirmar eliminación</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#delete-author-form"), async () => {
    await api(`/api/autores/${a.id}`, "DELETE");
    notify("Autor eliminado.");
    modal.close();
    authorsView();
  });
}

// ==================== VISTA: CATEGORÍAS ====================
async function categoriesView() {
  const revision = ++viewRevision;
  const res = await api(`/api/categorias?page=${page}&pageSize=20`);
  if (revision !== viewRevision) return;

  const actionBtn = canManageCatalog()
    ? `<button class="button primary" id="new-category">+ Nueva categoría</button>`
    : "";

  main.innerHTML = heading("Gestión de Categorías", "Clasificación de libros por temática y género.", actionBtn) + `
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>CATEGORÍA</th>
            <th>DESCRIPCIÓN</th>
            <th>LIBROS</th>
            <th>ACCIONES</th>
          </tr>
        </thead>
        <tbody>
          ${res.items.map((c) => `
            <tr>
              <td><strong>${esc(c.nombre)}</strong></td>
              <td>${esc(c.descripcion || "Sin descripción")}</td>
              <td><span class="pill">${c.totalLibros} libros</span></td>
              <td>
                <div class="card-actions">
                  <button class="button secondary small" data-category-books="${c.id}">Ver libros</button>
                  ${canManageCatalog() ? `
                    <button class="button secondary small" data-edit-category="${c.id}">Editar</button>
                    <button class="button danger small" data-del-category="${c.id}">Eliminar</button>
                  ` : ""}
                </div>
              </td>
            </tr>
          `).join("")}
        </tbody>
      </table>
    </div>
    ${pagination(res.total, 20, categoriesView)}
  `;

  if (canManageCatalog()) {
    document.querySelector("#new-category").onclick = () => categoryModal();
  }

  main.querySelectorAll("[data-category-books]").forEach((btn) => {
    btn.onclick = () => {
      currentCategory = btn.dataset.categoryBooks;
      location.hash = "catalogo";
    };
  });

  if (canManageCatalog()) {
    main.querySelectorAll("[data-edit-category]").forEach((btn) => {
      btn.onclick = () => {
        const item = res.items.find((x) => x.id === +btn.dataset.editCategory);
        categoryModal(item);
      };
    });
    main.querySelectorAll("[data-del-category]").forEach((btn) => {
      btn.onclick = () => {
        const item = res.items.find((x) => x.id === +btn.dataset.delCategory);
        deleteCategoryModal(item);
      };
    });
  }

  bindPaging(categoriesView);
}

function categoryModal(c = null) {
  openModal(c ? "Editar categoría" : "Nueva categoría", `
    <form id="category-form">
      <label>Nombre de la categoría *
        <input name="nombre" required maxlength="100" value="${esc(c?.nombre)}">
      </label>
      <label>Descripción
        <input name="descripcion" maxlength="300" value="${esc(c?.descripcion)}">
      </label>
      <div class="form-actions">
        <button type="submit" class="button primary">Guardar categoría</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#category-form"), async (f) => {
    const data = {
      nombre: f.get("nombre")?.trim(),
      descripcion: f.get("descripcion")?.trim() || null
    };

    if (c) {
      await api(`/api/categorias/${c.id}`, "PUT", data);
      notify("Categoría actualizada.");
    } else {
      await api("/api/categorias", "POST", data);
      notify("Categoría creada.");
    }
    modal.close();
    categoriesView();
  });
}

function deleteCategoryModal(c) {
  openModal("Eliminar categoría", `
    <form id="delete-category-form">
      <p>¿Deseas eliminar la categoría <strong>${esc(c.nombre)}</strong>?</p>
      <p class="help">Si la categoría contiene libros asignados, la API responderá con 409 Conflict.</p>
      <div class="form-actions">
        <button type="submit" class="button danger">Confirmar eliminación</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#delete-category-form"), async () => {
    await api(`/api/categorias/${c.id}`, "DELETE");
    notify("Categoría eliminada.");
    modal.close();
    categoriesView();
  });
}

// ==================== VISTA: LECTORES ====================
async function readersView() {
  const revision = ++viewRevision;
  if (!user || !canManageLoans()) {
    main.innerHTML = heading("Acceso restringido", "Solo el personal de biblioteca puede administrar lectores.");
    return;
  }

  const res = await api(`/api/lectores?page=${page}&pageSize=20`);
  if (revision !== viewRevision) return;

  main.innerHTML = heading("Gestión de Lectores", "Administración de usuarios autorizados para préstamos.", `
    <button class="button primary" id="new-reader">+ Nuevo lector</button>
  `) + `
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>NOMBRE</th>
            <th>CORREO</th>
            <th>TELÉFONO</th>
            <th>ESTADO</th>
            <th>PRÉSTAMOS ACTIVOS</th>
            <th>ACCIONES</th>
          </tr>
        </thead>
        <tbody>
          ${res.items.map((l) => `
            <tr>
              <td><strong>${esc(l.nombre)}</strong></td>
              <td>${esc(l.correo)}</td>
              <td>${esc(l.telefono || "—")}</td>
              <td>
                <span class="badge ${l.estaHabilitado ? "" : "danger"}">
                  ${l.estaHabilitado ? "Habilitado" : "Inhabilitado"}
                </span>
              </td>
              <td><span class="pill">${l.prestamosActivos} activos</span></td>
              <td>
                <div class="card-actions">
                  <button class="button secondary small" data-edit-reader="${l.id}">Editar</button>
                  <button class="button secondary small" data-toggle-reader="${l.id}">
                    ${l.estaHabilitado ? "Inhabilitar" : "Habilitar"}
                  </button>
                </div>
              </td>
            </tr>
          `).join("")}
        </tbody>
      </table>
    </div>
    ${pagination(res.total, 20, readersView)}
  `;

  document.querySelector("#new-reader").onclick = () => readerModal();

  main.querySelectorAll("[data-edit-reader]").forEach((btn) => {
    btn.onclick = () => {
      const item = res.items.find((x) => x.id === +btn.dataset.editReader);
      readerModal(item);
    };
  });

  main.querySelectorAll("[data-toggle-reader]").forEach((btn) => {
    btn.onclick = async () => {
      const item = res.items.find((x) => x.id === +btn.dataset.toggleReader);
      try {
        await api(`/api/lectores/${item.id}`, "PATCH", { estaHabilitado: !item.estaHabilitado });
        notify(`Lector ${!item.estaHabilitado ? "habilitado" : "inhabilitado"}.`);
        readersView();
      } catch (err) {
        notify(err.message, true);
      }
    };
  });

  bindPaging(readersView);
}

function readerModal(l = null) {
  openModal(l ? "Editar lector" : "Nuevo lector", `
    <form id="reader-form">
      <label>Nombre completo *
        <input name="nombre" required maxlength="150" value="${esc(l?.nombre)}">
      </label>
      <label>Correo electrónico *
        <input name="correo" type="email" required maxlength="200" value="${esc(l?.correo)}">
      </label>
      <label>Teléfono (opcional)
        <input name="telefono" maxlength="30" value="${esc(l?.telefono)}">
      </label>
      <label class="checkbox" style="margin-top:10px;">
        <input name="estaHabilitado" type="checkbox" ${l ? (l.estaHabilitado ? "checked" : "") : "checked"}>
        Lector habilitado para solicitar libros
      </label>
      <div class="form-actions">
        <button type="submit" class="button primary">Guardar lector</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#reader-form"), async (f) => {
    const data = {
      nombre: f.get("nombre")?.trim(),
      correo: f.get("correo")?.trim(),
      telefono: f.get("telefono")?.trim() || null,
      estaHabilitado: f.get("estaHabilitado") === "on"
    };

    if (l) {
      await api(`/api/lectores/${l.id}`, "PUT", data);
      notify("Lector actualizado.");
    } else {
      await api("/api/lectores", "POST", data);
      notify("Lector registrado.");
    }
    modal.close();
    readersView();
  });
}

// ==================== VISTA: PRÉSTAMOS ====================
async function loansView() {
  const revision = ++viewRevision;
  if (!user) {
    main.innerHTML = heading("Préstamos", "Debes iniciar sesión para consultar préstamos.") + `
      <div class="panel auth-panel"><a class="button primary" href="#login">Iniciar sesión</a></div>
    `;
    return;
  }

  let endpoint = "/api/prestamos";
  if (currentLoanTab === "activos") endpoint = "/api/prestamos/activos";
  else if (currentLoanTab === "vencidos") endpoint = "/api/prestamos/vencidos";

  const res = await api(`${endpoint}?page=${page}&pageSize=20`);
  if (revision !== viewRevision) return;

  const actionBtn = canManageLoans()
    ? `<button class="button primary" id="btn-nuevo-prestamo">+ Registrar préstamo</button>`
    : "";

  main.innerHTML = heading(
    isReader() && !canManageLoans() ? "Mis Préstamos" : "Gestión de Préstamos",
    "Seguimiento de entregas, devoluciones y cancelaciones de libros.",
    actionBtn
  ) + `
    <div class="tabs">
      <button class="tab-btn ${currentLoanTab === "activos" ? "active" : ""}" data-tab="activos">Préstamos Activos</button>
      <button class="tab-btn ${currentLoanTab === "vencidos" ? "active" : ""}" data-tab="vencidos">Vencidos</button>
      <button class="tab-btn ${currentLoanTab === "todos" ? "active" : ""}" data-tab="todos">Todo el historial</button>
    </div>

    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>LIBRO</th>
            <th>LECTOR</th>
            <th>FECHA PRÉSTAMO</th>
            <th>LÍMITE</th>
            <th>ESTADO</th>
            ${canManageLoans() ? `<th>ACCIONES</th>` : ""}
          </tr>
        </thead>
        <tbody>
          ${res.items.length ? res.items.map((p) => `
            <tr>
              <td><a href="#libro/${p.libroId}"><strong>${esc(p.libroTitulo)}</strong></a></td>
              <td>${esc(p.lectorNombre)}</td>
              <td>${date(p.fechaPrestamo)}</td>
              <td>${date(p.fechaLimite)}</td>
              <td>
                <span class="badge ${p.estado === "Activo" ? "busy" : p.estado === "Devuelto" ? "" : "danger"}">
                  ${p.estado}
                </span>
                ${p.fechaDevolucion ? `<small>Devuelto: ${date(p.fechaDevolucion)}</small>` : ""}
              </td>
              ${canManageLoans() ? `
                <td>
                  <div class="card-actions">
                    ${p.estado === "Activo" ? `
                      <button class="button secondary small" data-return="${p.id}">Devolver (PATCH)</button>
                      <button class="button danger small" data-cancel="${p.id}">Cancelar</button>
                    ` : "—"}
                  </div>
                </td>
              ` : ""}
            </tr>
          `).join("") : '<tr><td colspan="6" style="text-align:center;padding:30px;">No hay préstamos en este apartado.</td></tr>'}
        </tbody>
      </table>
    </div>
    ${pagination(res.total, 20, loansView)}
  `;

  main.querySelectorAll(".tab-btn").forEach((btn) => {
    btn.onclick = () => {
      currentLoanTab = btn.dataset.tab;
      page = 1;
      loansView().catch(showError);
    };
  });

  if (canManageLoans()) {
    const nuevoBtn = document.querySelector("#btn-nuevo-prestamo");
    if (nuevoBtn) nuevoBtn.onclick = () => loanModal();

    main.querySelectorAll("[data-return]").forEach((btn) => {
      btn.onclick = () => {
        const item = res.items.find((x) => x.id === +btn.dataset.return);
        returnModal(item);
      };
    });

    main.querySelectorAll("[data-cancel]").forEach((btn) => {
      btn.onclick = () => {
        const item = res.items.find((x) => x.id === +btn.dataset.cancel);
        cancelModal(item);
      };
    });
  }

  bindPaging(loansView);
}

// Modal: Registrar préstamo
async function loanModal(preselectedBook = null) {
  const [books, readers] = await Promise.all([
    preselectedBook ? { items: [preselectedBook] } : api("/api/libros/disponibles?pageSize=100"),
    api("/api/lectores?habilitado=true&pageSize=100")
  ]);

  openModal("Registrar Préstamo", `
    <form id="loan-form">
      <label>Libro a prestar *
        <select name="libroId" required ${preselectedBook ? "disabled" : ""}>
          ${books.items.map((b) => `<option value="${b.id}" ${preselectedBook?.id === b.id ? "selected" : ""}>${esc(b.titulo)} (${esc(b.autorNombre)})</option>`).join("")}
        </select>
      </label>
      <label>Lector habilitado *
        <select name="lectorId" required>
          <option value="">Selecciona un lector</option>
          ${readers.items.map((l) => `<option value="${l.id}">${esc(l.nombre)} (${esc(l.correo)})</option>`).join("")}
        </select>
      </label>
      <label>Días de préstamo (plazo de entrega)
        <input name="diasPrestamo" type="number" min="1" max="90" value="14" required>
      </label>
      <label>Observaciones (opcional)
        <input name="observaciones" maxlength="500" placeholder="Estado del libro, notas...">
      </label>
      <div class="form-actions">
        <button type="submit" class="button primary">Confirmar préstamo</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#loan-form"), async (f) => {
    const selectedBookId = preselectedBook ? preselectedBook.id : +f.get("libroId");
    const book = preselectedBook || books.items.find((x) => x.id === selectedBookId);

    const data = {
      libroId: selectedBookId,
      lectorId: +f.get("lectorId"),
      diasPrestamo: +f.get("diasPrestamo"),
      observaciones: f.get("observaciones")?.trim() || null,
      rowVersion: book?.rowVersion || null
    };

    await api("/api/prestamos", "POST", data);
    notify("Préstamo registrado exitosamente.");
    modal.close();
    location.hash = "prestamos";
  });
}

// Modal: Devolución con PATCH
function returnModal(p) {
  openModal("Registrar Devolución", `
    <form id="return-form">
      <p>Confirmar la devolución de: <strong>${esc(p.libroTitulo)}</strong></p>
      <p class="muted">Prestado a: ${esc(p.lectorNombre)}</p>
      <label>Observaciones de entrega (opcional)
        <input name="observaciones" placeholder="Buen estado, etc.">
      </label>
      <p class="help">Se ejecutará <code>PATCH /api/prestamos/${p.id}/devolucion</code> liberando la disponibilidad del libro.</p>
      <div class="form-actions">
        <button type="submit" class="button primary">Registrar devolución</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#return-form"), async (f) => {
    await api(`/api/prestamos/${p.id}/devolucion`, "PATCH", {
      observaciones: f.get("observaciones")?.trim() || null,
      rowVersion: p.libroRowVersion || null
    });
    notify("Devolución registrada correctamente.");
    modal.close();
    loansView();
  });
}

// Modal: Cancelación con PATCH
function cancelModal(p) {
  openModal("Cancelar Préstamo", `
    <form id="cancel-form">
      <p>¿Cancelar el préstamo de <strong>${esc(p.libroTitulo)}</strong>?</p>
      <label>Motivo de la cancelación *
        <input name="motivo" required placeholder="Error de registro, solicitud cancelada...">
      </label>
      <p class="help">Se ejecutará <code>PATCH /api/prestamos/${p.id}/cancelacion</code> y el libro volverá a estar disponible.</p>
      <div class="form-actions">
        <button type="submit" class="button danger">Confirmar cancelación</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#cancel-form"), async (f) => {
    await api(`/api/prestamos/${p.id}/cancelacion`, "PATCH", {
      motivo: f.get("motivo")?.trim(),
      rowVersion: p.libroRowVersion || null
    });
    notify("Préstamo cancelado.");
    modal.close();
    loansView();
  });
}

// ==================== VISTA: USUARIOS (ADMIN) ====================
async function usersView() {
  const revision = ++viewRevision;
  if (!user || !isAdmin()) {
    main.innerHTML = heading("Acceso Restringido", "Solo el Administrador puede gestionar usuarios del sistema.");
    return;
  }

  const res = await api(`/api/usuarios?page=${page}&pageSize=20`);
  if (revision !== viewRevision) return;

  main.innerHTML = heading("Gestión de Usuarios", "Cuentas de acceso y roles del sistema.", `
    <button class="button primary" id="new-user">+ Nuevo usuario</button>
  `) + `
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>NOMBRE / CORREO</th>
            <th>ROL</th>
            <th>ESTADO</th>
            <th>ACCIONES</th>
          </tr>
        </thead>
        <tbody>
          ${res.items.map((u) => `
            <tr>
              <td>
                <strong>${esc(u.nombreCompleto || "—")}</strong>
                <small>${esc(u.email)}</small>
              </td>
              <td><span class="pill">${esc(u.roles.join(", "))}</span></td>
              <td><span class="badge ${u.isActive ? "" : "danger"}">${u.isActive ? "Activo" : "Inactivo"}</span></td>
              <td>
                ${u.id === user.id ? '<span class="muted">Tu cuenta actual</span>' : `
                  <button class="button secondary small" data-edit-user="${u.id}">Editar rol/estado</button>
                `}
              </td>
            </tr>
          `).join("")}
        </tbody>
      </table>
    </div>
    ${pagination(res.total, 20, usersView)}
  `;

  document.querySelector("#new-user").onclick = () => userModal();

  main.querySelectorAll("[data-edit-user]").forEach((btn) => {
    btn.onclick = () => {
      const item = res.items.find((x) => x.id === btn.dataset.editUser);
      userModal(item);
    };
  });

  bindPaging(usersView);
}

function userModal(u = null) {
  openModal(u ? "Editar usuario" : "Nuevo usuario", `
    <form id="user-form">
      <label>Nombre completo
        <input name="nombreCompleto" maxlength="150" value="${esc(u?.nombreCompleto)}">
      </label>
      <label>Correo electrónico *
        <input name="email" type="email" required maxlength="200" value="${esc(u?.email)}" ${u ? "disabled" : ""}>
      </label>
      ${!u ? `
        <label>Contraseña *
          <input name="password" type="password" required minlength="8" maxlength="128">
        </label>
      ` : ""}
      <label>Rol asignado *
        <select name="role" required>
          <option value="Lector" ${u?.roles.includes("Lector") ? "selected" : ""}>Lector</option>
          <option value="Bibliotecario" ${u?.roles.includes("Bibliotecario") ? "selected" : ""}>Bibliotecario</option>
          <option value="Administrador" ${u?.roles.includes("Administrador") ? "selected" : ""}>Administrador</option>
        </select>
      </label>
      ${u ? `
        <label class="checkbox" style="margin-top:10px;">
          <input name="isActive" type="checkbox" ${u.isActive ? "checked" : ""}>
          Cuenta activa
        </label>
      ` : ""}
      <div class="form-actions">
        <button type="submit" class="button primary">Guardar usuario</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#user-form"), async (f) => {
    if (u) {
      await api(`/api/usuarios/${u.id}`, "PUT", {
        role: f.get("role"),
        isActive: f.get("isActive") === "on",
        nombreCompleto: f.get("nombreCompleto")?.trim() || null
      });
      notify("Usuario actualizado.");
    } else {
      await api("/api/usuarios", "POST", {
        email: f.get("email")?.trim(),
        password: f.get("password"),
        role: f.get("role"),
        nombreCompleto: f.get("nombreCompleto")?.trim() || null
      });
      notify("Usuario registrado.");
    }
    modal.close();
    usersView();
  });
}

// ==================== AUTH: LOGIN / REGISTRO / CUENTA ====================
function authForm(isRegister) {
  main.innerHTML = `
    <div class="panel auth-panel">
      <div class="eyebrow">BIBLIOTECA VIRTUAL · P3</div>
      <h1>${isRegister ? "Crear cuenta" : "Iniciar sesión"}</h1>
      <p class="muted">${isRegister ? "Regístrate como Lector para consultar y solicitar préstamos." : "Ingresa tus credenciales para acceder a la API."}</p>

      <form id="auth-form">
        ${isRegister ? `
          <label>Nombre completo *
            <input name="nombre" required maxlength="150" placeholder="Ej. Mario Alabi">
          </label>
          <label>Teléfono (opcional)
            <input name="telefono" maxlength="30" placeholder="7000-0000">
          </label>
        ` : ""}
        <label>Correo electrónico *
          <input name="email" type="email" required maxlength="200" placeholder="usuario@correo.com">
        </label>
        <label>Contraseña *
          <input name="password" type="password" required minlength="8" maxlength="128">
        </label>
        <button class="button primary" type="submit" style="width:100%;margin-top:15px;">
          ${isRegister ? "Registrarme" : "Entrar a la biblioteca"} ↗
        </button>
      </form>

      <p class="help" style="margin-top:20px;text-align:center;">
        ${isRegister
          ? '¿Ya tienes cuenta? <a class="text-link" href="#login">Inicia sesión</a>'
          : '¿No tienes cuenta? <a class="text-link" href="#registro">Crea una cuenta aquí</a>'}
      </p>
    </div>
  `;

  submitForm(document.querySelector("#auth-form"), async (f) => {
    const endpoint = isRegister ? "/api/auth/registro" : "/api/auth/login";
    const data = Object.fromEntries(f);
    const res = await api(endpoint, "POST", data);

    token = res.token;
    user = {
      id: res.id,
      email: res.email,
      nombre: res.nombre,
      roles: res.roles,
      lectorId: res.lectorId
    };

    localStorage.setItem("biblioteca_jwt", token);
    localStorage.setItem("biblioteca_user", JSON.stringify(user));

    updateNavigation();
    notify(isRegister ? "¡Cuenta creada exitosamente!" : "Sesión iniciada.");
    location.hash = "catalogo";
  });
}

function accountView() {
  if (!user) {
    location.hash = "login";
    return;
  }

  main.innerHTML = heading("Mi cuenta", "Información de tu perfil y credenciales.") + `
    <div class="panel auth-panel">
      <div class="eyebrow">PERFIL AUTENTICADO</div>
      <h2 style="margin-top:5px;">${esc(user.nombre || user.email)}</h2>
      <p class="muted">Correo: <strong>${esc(user.email)}</strong></p>
      <p>Rol: <span class="pill">${esc(user.roles?.join(", "))}</span></p>
      ${user.lectorId ? `<p>Ficha de Lector asociada: <strong>#${user.lectorId}</strong></p>` : ""}

      <div style="margin-top:30px;display:flex;gap:12px;">
        <button class="button danger" id="btn-logout">Cerrar sesión ↗</button>
        <button class="button secondary" id="btn-change-api">Configurar API</button>
      </div>
    </div>
  `;

  document.querySelector("#btn-logout").onclick = () => logout(true);
  document.querySelector("#btn-change-api").onclick = openApiConfigModal;
}

function logout(notifyUser = true) {
  token = "";
  user = null;
  localStorage.removeItem("biblioteca_jwt");
  localStorage.removeItem("biblioteca_user");
  updateNavigation();
  if (notifyUser) notify("Sesión cerrada.");
  location.hash = "inicio";
}

function openApiConfigModal() {
  openModal("Configuración del Endpoint API", `
    <form id="api-config-form">
      <p>Especifica la dirección base de la API REST que consume este cliente:</p>
      <label>URL de la API REST
        <input name="apiUrl" required value="${esc(API)}">
      </label>
      <p class="help">Predeterminado: <code>http://localhost:5081</code></p>
      <div class="form-actions">
        <button type="submit" class="button primary">Guardar URL</button>
      </div>
    </form>
  `);

  submitForm(document.querySelector("#api-config-form"), async (f) => {
    API = f.get("apiUrl").trim().replace(/\/$/, "");
    localStorage.setItem("biblioteca_api_url", API);
    modal.close();
    notify("URL de la API actualizada.");
    checkApiStatus();
    route();
  });
}

// ==================== ENRUTADOR CLIENTE (SPA) ====================
async function route() {
  modal.close();
  const [hashPath, id] = (location.hash.slice(1) || "inicio").split("/");

  const pageLabels = {
    inicio: "Inicio",
    catalogo: "Catálogo",
    libro: "Detalles del libro",
    autores: "Autores",
    categorias: "Categorías",
    lectores: "Lectores",
    prestamos: "Préstamos",
    usuarios: "Usuarios",
    cuenta: "Mi cuenta",
    login: "Iniciar sesión",
    registro: "Crear cuenta"
  };

  document.querySelector("#page-label").textContent = pageLabels[hashPath] || "Inicio";

  document.querySelectorAll("[data-nav]").forEach((a) => {
    const navName = a.dataset.nav;
    a.classList.toggle("active", navName === hashPath || (hashPath === "libro" && navName === "catalogo"));
  });

  main.innerHTML = '<div class="empty">Cargando…</div>';

  try {
    switch (hashPath) {
      case "catalogo":
        await catalog();
        break;
      case "libro":
        await detail(Number(id));
        break;
      case "autores":
        await authorsView();
        break;
      case "categorias":
        await categoriesView();
        break;
      case "lectores":
        await readersView();
        break;
      case "prestamos":
        await loansView();
        break;
      case "usuarios":
        await usersView();
        break;
      case "cuenta":
        accountView();
        break;
      case "login":
        authForm(false);
        break;
      case "registro":
        authForm(true);
        break;
      default:
        await home();
        break;
    }
  } catch (err) {
    if ((location.hash.slice(1) || "inicio").split("/")[0] === hashPath) {
      showError(err);
    }
  }
}

window.addEventListener("hashchange", () => {
  page = 1;
  route();
});

// Inicialización
(async () => {
  updateNavigation();
  checkApiStatus();
  await route();
})();
