# Biblioteca Virtual · API REST (Periodo 3)

Sistema web de gestión de biblioteca basado en la propuesta oficial del Periodo 3. El núcleo del sistema es una **API REST desacoplada** desarrollada con **ASP.NET Core Web API y .NET 10**, con persistencia en **MariaDB**, autenticación **JWT**, control de concurrencia optimista, carga de portadas (`multipart/form-data`), documentación **Swagger / OpenAPI** y un **cliente web desacoplado (Single Page Application)**.

---

## 🏛️ Arquitectura Desacoplada

* **Backend (`src/Biblioteca.Api`):**
  * ASP.NET Core Web API (.NET 10).
  * Exposición de servicios REST en formato JSON.
  * Autenticación basada en tokens JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`).
  * Persistencia con Entity Framework Core 9 y MariaDB 11.4 (`Pomelo.EntityFrameworkCore.MySql`).
  * Documentación interactiva Swagger UI con soporte para tokens Bearer.
  * Carga y servicio de imágenes estáticas en `/uploads/portadas/`.
  * Totalmente independiente del frontend; no contiene vistas Razor ni dependencias de interfaz.

* **Frontend (`src/Biblioteca.Web`):**
  * Cliente web SPA (Single Page Application) en HTML5, CSS y JavaScript moderno.
  * Consumo exclusivo de la API REST mediante solicitudes HTTP (`fetch`) enviando el header `Authorization: Bearer <token>`.
  * Sin lógica de base de datos ni acoplamiento MVC: puede alojarse en Nginx, Apache, S3 o servidores estáticos.

---

## 🚀 Arranque Rápido

### Requisitos
* SDK de **.NET 10** (`~/.dotnet/dotnet` o en el `PATH`).
* Conexión a la base de datos MariaDB (configurada en `.env` o en `$HOME/Documents/Codex/.secrets/biblioteca-virtual.env`).
* Base de datos utilizada para el Periodo 3: `biblioteca_virtual_p3`.

### Ejecutar en desarrollo

```bash
./scripts/dev.sh
```

El script compilará la solución, aplicará las migraciones en MariaDB, inicializará el administrador y los datos semilla (`--seed`), y levantará los servicios:

* **Cliente Web (Frontend):** [http://localhost:5080](http://localhost:5080)
* **API REST (Backend):** [http://localhost:5081](http://localhost:5081)
* **Documentación Swagger UI:** [http://localhost:5081/swagger](http://localhost:5081/swagger)

Para detener ambos servicios: presiona `Ctrl + C`.

---

## 👥 Usuarios y Roles Preconfigurados

El sistema implementa 3 roles: **Administrador**, **Bibliotecario** y **Lector**.

| Rol | Catálogo (Libros, Autores, Categorías) | Lectores | Préstamos | Usuarios / Roles |
|---|---|---|---|---|
| **Administrador** | Consulta y Gestión (CRUD) | Gestión completa | Gestión completa | Crear, editar y cambiar roles |
| **Bibliotecario** | Consulta y Gestión (CRUD) | Gestión completa | Préstamos, devoluciones, cancelaciones | Sin acceso |
| **Lector** | Consulta y disponibilidad | Ver solo su propia ficha | Ver solo sus propios préstamos | Sin acceso |

### Credenciales iniciales
* **Administrador:** `admin@biblioteca.test` / `Admin1234!`
* **Registro público (`POST /api/auth/registro`):** Crea usuarios con rol **Lector** y genera automáticamente su ficha en la tabla `Lectores`.

---

## 📚 Endpoints de la API REST

### 1. Autenticación (`/api/auth`)
* `POST /api/auth/registro` - Registro público de lectores con creación automática de ficha.
* `POST /api/auth/login` - Inicio de sesión y generación de token JWT Bearer.
* `GET /api/auth/me` - Consulta del perfil del usuario autenticado y roles.

### 2. Libros (`/api/libros`)
* `GET /api/libros` - Catálogo paginado con filtros query: `titulo`, `autorId`, `categoriaId`, `disponible`, `search`.
* `GET /api/libros/disponibles` - Libros listos para préstamo.
* `GET /api/libros/{id}` - Consulta por identificador con detalles del autor, categoría y `RowVersion`.
* `POST /api/libros` - Registro de libro (Bibliotecario / Admin).
* `PUT /api/libros/{id}` - Actualización total con validación de concurrencia (`RowVersion`).
* `PATCH /api/libros/{id}` - Modificación parcial con validación de concurrencia.
* `DELETE /api/libros/{id}` - Eliminación (rechaza con `409 Conflict` si posee préstamos).
* `POST /api/libros/{id}/imagen` - Carga de portada (`multipart/form-data`, JPG/PNG/WebP, máx 5MB).
* `DELETE /api/libros/{id}/imagen` - Eliminación de la portada asociada.

### 3. Autores (`/api/autores`)
* `GET /api/autores` - Lista paginada y búsqueda por nombre.
* `GET /api/autores/{id}` - Detalle de un autor.
* `GET /api/autores/{id}/libros` - Libros pertenecientes al autor.
* `POST /api/autores` - Registro de nuevo autor.
* `PUT /api/autores/{id}` - Actualización completa.
* `PATCH /api/autores/{id}` - Modificación parcial.
* `DELETE /api/autores/{id}` - Eliminación (rechaza con `409 Conflict` si posee libros en el catálogo).

### 4. Categorías (`/api/categorias`)
* `GET /api/categorias` - Lista paginada y búsqueda por nombre.
* `GET /api/categorias/{id}` - Detalle de categoría.
* `GET /api/categorias/{id}/libros` - Libros pertenecientes a la categoría.
* `POST /api/categorias` - Registro de nueva categoría.
* `PUT /api/categorias/{id}` - Actualización completa.
* `PATCH /api/categorias/{id}` - Modificación parcial.
* `DELETE /api/categorias/{id}` - Eliminación (rechaza con `409 Conflict` si posee libros).

### 5. Lectores (`/api/lectores`)
* `GET /api/lectores` - Lista paginada con filtros por nombre/correo y estado.
* `GET /api/lectores/{id}` - Consulta de lector.
* `POST /api/lectores` - Registro manual de lector (Bibliotecario / Admin).
* `PUT /api/lectores/{id}` - Actualización de datos y estado de habilitación.
* `PATCH /api/lectores/{id}` - Modificación parcial.
* `DELETE /api/lectores/{id}` - Eliminación (rechaza con `409 Conflict` si posee préstamos activos).
* `GET /api/lectores/{id}/prestamos` - Historial completo de préstamos del lector.
* `GET /api/lectores/{id}/prestamos/activos` - Préstamos vigentes del lector.

### 6. Préstamos (`/api/prestamos`)
* `GET /api/prestamos` - Lista paginada con filtros: `lectorId`, `libroId`, `desde`, `hasta`, `estado`.
* `GET /api/prestamos/activos` - Préstamos actualmente activos.
* `GET /api/prestamos/vencidos` - Préstamos activos cuya fecha límite ha expirado.
* `GET /api/prestamos/{id}` - Consulta de préstamo por ID.
* `POST /api/prestamos` - Registro de préstamo en transacción atómica: valida disponibilidad del libro, lector habilitado, marca disponibilidad en `false` y asigna estado `Activo`.
* `PATCH /api/prestamos/{id}/devolucion` - Registro de devolución: registra fecha de entrega, cambia estado a `Devuelto` y libera la disponibilidad del libro.
* `PATCH /api/prestamos/{id}/cancelacion` - Anulación del préstamo: registra motivo y reestablece disponibilidad del libro.

### 7. Administración de Usuarios (`/api/usuarios`)
* `GET /api/usuarios` - Listado de cuentas (solo Administrador).
* `POST /api/usuarios` - Creación de usuarios asignando roles directamente.
* `PUT /api/usuarios/{id}` - Cambio de rol y activación/desactivación de cuentas.

---

## 🔒 Control de Concurrencia MariaDB

Cada libro contiene un campo de versión `RowVersion` que se actualiza automáticamente con un nuevo `Guid` en cada guardado:
* Al ejecutar un `PUT` o `PATCH`, el cliente envía la versión que leyó previamente.
* Si otro usuario modificó el registro en el intervalo, la API rechaza la solicitud respondiendo con **`409 Conflict`**.
* La interfaz web ofrece un botón de recarga automática para refrescar los datos sin perder el contexto.

---

## 🧪 Pruebas Automatizadas

El proyecto incluye una suite de pruebas de integración completa sobre MariaDB y solicitudes HTTP reales:

```bash
python3 tests/run.py
```

La suite crea una base de datos temporal exclusiva de pruebas (`biblioteca_virtual_p3_*_tests`), aplica migraciones, crea el administrador, arranca la API y valida 16 escenarios:
1. Health checks y Swagger JSON.
2. Autenticación con JWT Bearer y endpoint `/api/auth/me`.
3. Registro de Lector con auto-creación de ficha en `Lectores`.
4. Creación de Bibliotecario por el Administrador.
5. Control de accesos y códigos `403 Forbidden` según rol.
6. Creación de Autores y Categorías.
7. Creación de Libros con relaciones y `RowVersion`.
8. Filtros de búsqueda (título, autor, categoría).
9. Detección de conflicto `409` al borrar autor/categoría con libros.
10. Detección de conflicto de concurrencia `409` en actualizaciones simultáneas.
11. Registro de préstamos y prevención a nivel de base de datos de doble préstamo.
12. Consulta de préstamos activos e historial.
13. Devolución mediante `PATCH` y liberación de disponibilidad.
14. Cancelación mediante `PATCH`.
15. Restricción `409` al eliminar libro con préstamos.
16. Subida (`multipart/form-data`) y entrega de imagen de portada.
