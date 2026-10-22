#!/usr/bin/env python3
"""Pruebas de integración HTTP + MariaDB para Biblioteca Virtual (Periodo 3).
Verifica:
1. OpenAPI / Swagger y Health checks
2. Autenticación JWT y Perfiles
3. Tres roles (Administrador, Bibliotecario, Lector)
4. Creación y consulta de Autores, Categorías, Libros y Lectores
5. Filtros de búsqueda (título, autor, categoría, disponibilidad)
6. Control de concurrencia optimista (RowVersion -> 409 Conflict)
7. Reglas de negocio de Préstamos (Disponibilidad, Estado Activo, Devolución PATCH, Cancelación PATCH)
8. Restricciones de integridad referencial (409 al borrar entidades con dependencias)
"""

import json, os, urllib.request, urllib.error, urllib.parse, uuid

URL = os.environ.get('TEST_API_URL', 'http://localhost:5181')

class RestClient:
    def __init__(self, token=None):
        self.token = token

    def call(self, method, path, data=None, expected=200):
        headers = {}
        if self.token:
            headers['Authorization'] = f'Bearer {self.token}'
        if data is not None:
            headers['Content-Type'] = 'application/json'

        body = json.dumps(data).encode('utf-8') if data is not None else None
        safe_path = urllib.parse.quote(path, safe='/?=&:')
        req = urllib.request.Request(URL + safe_path, data=body, headers=headers, method=method)

        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                status = resp.status
                raw = resp.read()
                content_type = resp.headers.get('Content-Type', '')
        except urllib.error.HTTPError as e:
            status = e.code
            raw = e.read()
            content_type = e.headers.get('Content-Type', '')

        result = json.loads(raw.decode('utf-8')) if raw and 'application/json' in content_type else None
        assert status == expected, f"Falla en {method} {path}: status={status}, esperado={expected}, body={result or raw[:200]}"
        return result

    def login(self, email, password):
        res = self.call('POST', '/api/auth/login', {'email': email, 'password': password})
        self.token = res['token']
        return res

passed = []
def check(name):
    passed.append(name)
    print(f"✓ PASS: {name}", flush=True)

# 1. Health checks y Swagger
anon = RestClient()
res_live = anon.call('GET', '/health/live')
assert res_live['status'] == 'ok'
res_ready = anon.call('GET', '/health/ready')
assert res_ready['status'] == 'ready'
res_swagger = anon.call('GET', '/swagger/v1/swagger.json')
assert 'openapi' in res_swagger or 'swagger' in res_swagger
check("Health checks y documentación Swagger/OpenAPI disponibles")

# 2. Login de Administrador inicial
admin = RestClient()
admin_email = os.environ.get('TEST_ADMIN_EMAIL', 'admin@biblioteca.test')
admin_pass = os.environ.get('TEST_ADMIN_PASSWORD', 'Admin1234!')
login_res = admin.login(admin_email, admin_pass)
assert 'Administrador' in login_res['roles']
assert len(login_res['token']) > 20

# 3. /api/auth/me con JWT
me = admin.call('GET', '/api/auth/me')
assert me['email'] == admin_email
assert 'Administrador' in me['roles']
check("Autenticación con JWT Bearer y endpoint /api/auth/me")

# 4. Registro público de nuevo Lector
prefix = "QA-" + uuid.uuid4().hex[:8]
reader_client = RestClient()
reader_email = f"{prefix}-lector@example.test"
reader_pass = "LectorPass123!"
reg_res = reader_client.call('POST', '/api/auth/registro', {
    'email': reader_email,
    'password': reader_pass,
    'nombre': f'Lector {prefix}',
    'telefono': '7000-9999'
})
assert 'Lector' in reg_res['roles']
assert reg_res['lectorId'] is not None
reader_client.token = reg_res['token']
check("Registro de nuevo usuario con rol Lector y creación automática de ficha Lector")

# 5. Admin crea un Bibliotecario
lib_email = f"{prefix}-biblio@example.test"
lib_user = admin.call('POST', '/api/usuarios', {
    'email': lib_email,
    'password': 'BiblioPass123!',
    'role': 'Bibliotecario',
    'nombreCompleto': f'Bibliotecario {prefix}'
}, 201)
librarian = RestClient()
librarian.login(lib_email, 'BiblioPass123!')
check("Administrador crea un usuario con rol Bibliotecario")

# 6. Verificación de permisos y aislamiento de roles (403 Forbidden)
# El lector no puede listar ni crear usuarios en /api/usuarios
reader_client.call('GET', '/api/usuarios', expected=403)
reader_client.call('POST', '/api/usuarios', {'email': 'hack@example.test', 'password': 'pass', 'role': 'Administrador'}, 403)
# El lector no puede registrar libros
reader_client.call('POST', '/api/libros', {'titulo': 'Prohibido', 'anioPublicacion': 2026, 'autorId': 1, 'categoriaId': 1}, 403)
# El bibliotecario no puede administrar usuarios
librarian.call('GET', '/api/usuarios', expected=403)
check("Protección de endpoints según roles (Administrador, Bibliotecario, Lector)")

# 7. Bibliotecario crea Autor y Categoría
autor = librarian.call('POST', '/api/autores', {
    'nombre': f'Autor {prefix}',
    'nacionalidad': 'Salvadoreña',
    'fechaNacimiento': '1985-05-15'
}, 201)
assert autor['id'] > 0

categoria = librarian.call('POST', '/api/categorias', {
    'nombre': f'Categoría {prefix}',
    'descripcion': 'Libros para pruebas automatizadas'
}, 201)
assert categoria['id'] > 0
check("Creación de Autores y Categorías mediante la API")

# 8. Bibliotecario crea un Libro asociado
libro = librarian.call('POST', '/api/libros', {
    'titulo': f'Libro {prefix}',
    'anioPublicacion': 2026,
    'isbn': f'978-{uuid.uuid4().hex[:10]}',
    'autorId': autor['id'],
    'categoriaId': categoria['id']
}, 201)
assert libro['id'] > 0
assert libro['disponible'] is True
assert libro['autorId'] == autor['id']
assert libro['categoriaId'] == categoria['id']
assert len(libro['rowVersion']) == 32
check("Creación de Libro con relaciones e inicialización de RowVersion")

# 9. Filtros de búsqueda en libros
filtro_titulo = anon.call('GET', f'/api/libros?titulo={libro["titulo"]}')
assert filtro_titulo['total'] >= 1
assert any(x['id'] == libro['id'] for x in filtro_titulo['items'])

filtro_autor = anon.call('GET', f'/api/libros?autorId={autor["id"]}')
assert any(x['id'] == libro['id'] for x in filtro_autor['items'])

libros_autor = anon.call('GET', f'/api/autores/{autor["id"]}/libros')
assert any(x['id'] == libro['id'] for x in libros_autor)

libros_cat = anon.call('GET', f'/api/categorias/{categoria["id"]}/libros')
assert any(x['id'] == libro['id'] for x in libros_cat)
check("Búsqueda y navegación por filtros (título, autor, categoría, relaciones)")

# 10. Restricción de eliminación de Autor y Categoría con libros asociados (409 Conflict)
librarian.call('DELETE', f'/api/autores/{autor["id"]}', expected=409)
librarian.call('DELETE', f'/api/categorias/{categoria["id"]}', expected=409)
check("Validación de integridad: 409 Conflict al intentar borrar Autor o Categoría con libros")

# 11. Concurrencia optimista con RowVersion
# Cliente A obtiene el libro
client_a = RestClient(librarian.token)
book_a = client_a.call('GET', f'/api/libros/{libro["id"]}')
v1 = book_a['rowVersion']

# Cliente B actualiza el libro
client_b = RestClient(librarian.token)
update_b = client_b.call('PUT', f'/api/libros/{libro["id"]}', {
    'titulo': f'Libro {prefix} (Modificado por B)',
    'anioPublicacion': 2026,
    'isbn': libro['isbn'],
    'autorId': autor['id'],
    'categoriaId': categoria['id'],
    'rowVersion': v1
})
v2 = update_b['rowVersion']
assert v2 != v1, "La versión de concurrencia debe cambiar al modificarse el libro"

# Cliente A intenta modificar con la versión desactualizada v1 -> 409 Conflict
client_a.call('PUT', f'/api/libros/{libro["id"]}', {
    'titulo': f'Libro {prefix} (Intento desactualizado de A)',
    'anioPublicacion': 2026,
    'isbn': libro['isbn'],
    'autorId': autor['id'],
    'categoriaId': categoria['id'],
    'rowVersion': v1
}, expected=409)

# Actualización parcial PATCH con la versión v2 correcta
patch_res = client_b.call('PATCH', f'/api/libros/{libro["id"]}', {
    'titulo': f'Libro {prefix} (Vía PATCH)',
    'rowVersion': v2
})
assert patch_res['titulo'] == f'Libro {prefix} (Vía PATCH)'
v3 = patch_res['rowVersion']
assert v3 != v2
check("Control de concurrencia MariaDB: detección de conflicto 409 y actualización PUT/PATCH")

# 12. Préstamos: flujo completo, reglas de negocio y transacciones
lector_id = reg_res['lectorId']

# Bibliotecario registra préstamo para el lector
prestamo = librarian.call('POST', '/api/prestamos', {
    'libroId': libro['id'],
    'lectorId': lector_id,
    'diasPrestamo': 7,
    'observaciones': 'Préstamo para pruebas de integración'
}, 201)
assert prestamo['id'] > 0
assert prestamo['estado'] == 'Activo'

# Verificar que el libro ya no está disponible
libro_prestado = anon.call('GET', f'/api/libros/{libro["id"]}')
assert libro_prestado['disponible'] is False

# Intentar prestar el mismo libro mientras está prestado -> 409 Conflict
librarian.call('POST', '/api/prestamos', {
    'libroId': libro['id'],
    'lectorId': lector_id,
    'diasPrestamo': 7
}, expected=409)
check("Préstamo exitoso, libro marcado como no disponible y rechazo 409 a doble préstamo")

# Consultar préstamos activos
activos = librarian.call('GET', '/api/prestamos/activos')
assert any(x['id'] == prestamo['id'] for x in activos['items'])

# El lector puede consultar sus propios préstamos
mis_prestamos = reader_client.call('GET', f'/api/lectores/{lector_id}/prestamos')
assert any(x['id'] == prestamo['id'] for x in mis_prestamos)
check("Consulta de préstamos activos e historial de préstamos del lector")

# Registrar devolución mediante PATCH /api/prestamos/{id}/devolucion
devolucion = librarian.call('PATCH', f'/api/prestamos/{prestamo["id"]}/devolucion', {
    'observaciones': 'Entregado a tiempo en buen estado',
    'rowVersion': libro_prestado['rowVersion']
})
assert devolucion['estado'] == 'Devuelto'
assert devolucion['fechaDevolucion'] is not None

# Verificar que el libro vuelve a estar disponible
libro_devuelto = anon.call('GET', f'/api/libros/{libro["id"]}')
assert libro_devuelto['disponible'] is True

# Intentar devolver un préstamo ya devuelto -> 409 Conflict
librarian.call('PATCH', f'/api/prestamos/{prestamo["id"]}/devolucion', {}, expected=409)
check("Devolución mediante PATCH: libro disponible nuevamente y rechazo 409 si ya está devuelto")

# Cancelación de préstamo mediante PATCH /api/prestamos/{id}/cancelacion
# Creamos un segundo préstamo
prestamo2 = librarian.call('POST', '/api/prestamos', {
    'libroId': libro['id'],
    'lectorId': lector_id,
    'diasPrestamo': 3
}, 201)
assert prestamo2['estado'] == 'Activo'

# Cancelamos el préstamo
cancelacion = librarian.call('PATCH', f'/api/prestamos/{prestamo2["id"]}/cancelacion', {
    'motivo': 'Lector solicitó anulación del préstamo',
    'rowVersion': prestamo2['libroRowVersion']
})
assert cancelacion['estado'] == 'Cancelado'

# Verificar disponibilidad tras cancelación
libro_post_cancel = anon.call('GET', f'/api/libros/{libro["id"]}')
assert libro_post_cancel['disponible'] is True
check("Cancelación de préstamo mediante PATCH y reestablecimiento de disponibilidad")

# 13. Restricción al eliminar libro con historial de préstamos (409 Conflict)
librarian.call('DELETE', f'/api/libros/{libro["id"]}', expected=409)
check("Restricción de negocio: 409 Conflict al intentar borrar un libro con historial de préstamos")

# 14. Carga y eliminación de imagen de portada (multipart/form-data)
boundary = '----WebKitFormBoundary' + uuid.uuid4().hex
dummy_png = b'\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15c4\x00\x00\x00\nIDATx\x9cc\x00\x01\x00\x00\x05\x00\x01\r\n-\xb4\x00\x00\x00\x00IEND\xaeB`\x82'
body_parts = [
    f'--{boundary}\r\nContent-Disposition: form-data; name="imagen"; filename="portada.png"\r\nContent-Type: image/png\r\n\r\n'.encode('utf-8'),
    dummy_png,
    f'\r\n--{boundary}--\r\n'.encode('utf-8')
]
upload_data = b''.join(body_parts)

req_img = urllib.request.Request(
    f"{URL}/api/libros/{libro['id']}/imagen",
    data=upload_data,
    headers={
        'Authorization': f'Bearer {librarian.token}',
        'Content-Type': f'multipart/form-data; boundary={boundary}'
    },
    method='POST'
)
with urllib.request.urlopen(req_img, timeout=30) as r:
    assert r.status == 200
    res_img = json.loads(r.read().decode('utf-8'))
    assert res_img['portadaUrl'] is not None
    assert 'uploads/portadas' in res_img['portadaUrl']

# Comprobar que la imagen es accesible vía HTTP estático
req_static = urllib.request.Request(f"{URL}{res_img['portadaUrl']}", method='GET')
with urllib.request.urlopen(req_static, timeout=30) as r_stat:
    assert r_stat.status == 200
    assert r_stat.read() == dummy_png

# Eliminar la imagen
librarian.call('DELETE', f'/api/libros/{libro["id"]}/imagen', expected=204)
libro_sin_imagen = anon.call('GET', f'/api/libros/{libro["id"]}')
assert libro_sin_imagen['portadaUrl'] is None
check("Carga y eliminación de imagen de portada (multipart/form-data) y entrega estática")

print(f"\n=======================================================")
print(f"  TODAS LAS PRUEBAS ({len(passed)} verificaciones) COMPLETADAS CON ÉXITO")
print(f"=======================================================\n")
