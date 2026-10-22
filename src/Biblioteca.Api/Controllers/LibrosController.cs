using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Biblioteca.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Controllers;

[ApiController, Route("api/libros")]
public class LibrosController(LibraryDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private static LibroDto ToDto(Libro b) => new(
        b.Id,
        b.Titulo,
        b.AnioPublicacion,
        b.Isbn,
        b.Disponible,
        b.AutorId,
        b.Autor?.Nombre ?? string.Empty,
        b.CategoriaId,
        b.Categoria?.Nombre ?? string.Empty,
        b.PortadaUrl,
        b.RowVersion
    );

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? titulo,
        [FromQuery] int? autorId,
        [FromQuery] int? categoriaId,
        [FromQuery] bool? disponible,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.Libros.AsNoTracking()
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(titulo))
            q = q.Where(x => x.Titulo.Contains(titulo.Trim()));

        if (autorId.HasValue && autorId.Value > 0)
            q = q.Where(x => x.AutorId == autorId.Value);

        if (categoriaId.HasValue && categoriaId.Value > 0)
            q = q.Where(x => x.CategoriaId == categoriaId.Value);

        if (disponible.HasValue)
            q = q.Where(x => x.Disponible == disponible.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Titulo.Contains(s) ||
                             (x.Isbn != null && x.Isbn.Contains(s)) ||
                             x.Autor!.Nombre.Contains(s) ||
                             x.Categoria!.Nombre.Contains(s));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.Titulo)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return Ok(new PagedResult<LibroDto>(items, total, page, pageSize));
    }

    [HttpGet("disponibles")]
    public async Task<IActionResult> Disponibles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
    {
        return await Listar(null, null, null, true, null, page, pageSize, ct);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
    {
        var b = await db.Libros.AsNoTracking()
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        return b is null ? NotFound(new { message = "Libro no encontrado." }) : Ok(ToDto(b));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearLibroDto input, CancellationToken ct)
    {
        if (!await db.Autores.AnyAsync(x => x.Id == input.AutorId, ct))
            return BadRequest(new { message = "El autor especificado no existe." });

        if (!await db.Categorias.AnyAsync(x => x.Id == input.CategoriaId, ct))
            return BadRequest(new { message = "La categoría especificada no existe." });

        var libro = new Libro
        {
            Titulo = input.Titulo.Trim(),
            AnioPublicacion = input.AnioPublicacion,
            Isbn = string.IsNullOrWhiteSpace(input.Isbn) ? null : input.Isbn.Trim(),
            AutorId = input.AutorId,
            CategoriaId = input.CategoriaId,
            Disponible = true
        };

        db.Libros.Add(libro);
        await db.SaveChangesAsync(ct);

        // Recargar con relaciones para DTO
        var creado = await db.Libros.AsNoTracking()
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .SingleAsync(x => x.Id == libro.Id, ct);

        return CreatedAtAction(nameof(Obtener), new { id = libro.Id }, ToDto(creado));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarLibroDto input, CancellationToken ct)
    {
        var libro = await db.Libros
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (libro is null) return NotFound(new { message = "Libro no encontrado." });

        LibraryService.CheckVersion(libro, input.RowVersion);

        if (!await db.Autores.AnyAsync(x => x.Id == input.AutorId, ct))
            return BadRequest(new { message = "El autor especificado no existe." });

        if (!await db.Categorias.AnyAsync(x => x.Id == input.CategoriaId, ct))
            return BadRequest(new { message = "La categoría especificada no existe." });

        libro.Titulo = input.Titulo.Trim();
        libro.AnioPublicacion = input.AnioPublicacion;
        libro.Isbn = string.IsNullOrWhiteSpace(input.Isbn) ? null : input.Isbn.Trim();
        libro.AutorId = input.AutorId;
        libro.CategoriaId = input.CategoriaId;

        db.Entry(libro).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);

        var actualizado = await db.Libros.AsNoTracking()
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .SingleAsync(x => x.Id == libro.Id, ct);

        return Ok(ToDto(actualizado));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPatch("{id:int}")]
    public async Task<IActionResult> ModificarParcial(int id, [FromBody] PatchLibroDto input, CancellationToken ct)
    {
        var libro = await db.Libros
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (libro is null) return NotFound(new { message = "Libro no encontrado." });

        LibraryService.CheckVersion(libro, input.RowVersion);

        if (!string.IsNullOrWhiteSpace(input.Titulo))
            libro.Titulo = input.Titulo.Trim();

        if (input.AnioPublicacion.HasValue)
            libro.AnioPublicacion = input.AnioPublicacion.Value;

        if (input.Isbn is not null)
            libro.Isbn = string.IsNullOrWhiteSpace(input.Isbn) ? null : input.Isbn.Trim();

        if (input.AutorId.HasValue && input.AutorId.Value != libro.AutorId)
        {
            if (!await db.Autores.AnyAsync(x => x.Id == input.AutorId.Value, ct))
                return BadRequest(new { message = "El autor especificado no existe." });
            libro.AutorId = input.AutorId.Value;
        }

        if (input.CategoriaId.HasValue && input.CategoriaId.Value != libro.CategoriaId)
        {
            if (!await db.Categorias.AnyAsync(x => x.Id == input.CategoriaId.Value, ct))
                return BadRequest(new { message = "La categoría especificada no existe." });
            libro.CategoriaId = input.CategoriaId.Value;
        }

        if (input.Disponible.HasValue)
            libro.Disponible = input.Disponible.Value;

        db.Entry(libro).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);

        var actualizado = await db.Libros.AsNoTracking()
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .SingleAsync(x => x.Id == libro.Id, ct);

        return Ok(ToDto(actualizado));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, [FromQuery] string? rowVersion, [FromBody] VersionDto? body, CancellationToken ct)
    {
        var version = body?.RowVersion ?? rowVersion;
        var libro = await db.Libros.FindAsync([id], ct);
        if (libro is null) return NotFound(new { message = "Libro no encontrado." });

        LibraryService.CheckVersion(libro, version);

        if (await db.Prestamos.AnyAsync(x => x.LibroId == id, ct))
        {
            throw new DomainException(409, "No se puede eliminar un libro con historial de préstamos.");
        }

        // Eliminar imagen física si existía
        if (!string.IsNullOrEmpty(libro.PortadaUrl))
        {
            var relativePath = libro.PortadaUrl.TrimStart('/');
            var fullPath = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), relativePath);
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
        }

        db.Libros.Remove(libro);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPost("{id:int}/imagen")]
    public async Task<IActionResult> CargarImagen(int id, IFormFile? imagen, CancellationToken ct)
    {
        if (imagen is null || imagen.Length == 0)
            return BadRequest(new { message = "Debes proporcionar un archivo de imagen válido." });

        if (imagen.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "El tamaño de la imagen no puede superar los 5 MB." });

        var extension = Path.GetExtension(imagen.FileName).ToLowerInvariant();
        string[] permitidas = [".jpg", ".jpeg", ".png", ".webp"];
        if (!permitidas.Contains(extension))
            return BadRequest(new { message = "Formato no permitido. Usa JPG, PNG o WebP." });

        var libro = await db.Libros.FindAsync([id], ct);
        if (libro is null) return NotFound(new { message = "Libro no encontrado." });

        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var uploadsDir = Path.Combine(webRoot, "uploads", "portadas");
        Directory.CreateDirectory(uploadsDir);

        // Borrar anterior si existe
        if (!string.IsNullOrEmpty(libro.PortadaUrl))
        {
            var oldPath = Path.Combine(webRoot, libro.PortadaUrl.TrimStart('/'));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }

        var fileName = $"libro_{id}_{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await imagen.CopyToAsync(stream, ct);
        }

        libro.PortadaUrl = $"/uploads/portadas/{fileName}";
        db.Entry(libro).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);

        var actualizado = await db.Libros.AsNoTracking()
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .SingleAsync(x => x.Id == libro.Id, ct);

        return Ok(ToDto(actualizado));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpDelete("{id:int}/imagen")]
    public async Task<IActionResult> EliminarImagen(int id, CancellationToken ct)
    {
        var libro = await db.Libros.FindAsync([id], ct);
        if (libro is null) return NotFound(new { message = "Libro no encontrado." });

        if (!string.IsNullOrEmpty(libro.PortadaUrl))
        {
            var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
            var fullPath = Path.Combine(webRoot, libro.PortadaUrl.TrimStart('/'));
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);

            libro.PortadaUrl = null;
            db.Entry(libro).State = EntityState.Modified;
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}
