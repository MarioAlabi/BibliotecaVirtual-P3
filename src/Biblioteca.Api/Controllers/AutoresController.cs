using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Biblioteca.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Controllers;

[ApiController, Route("api/autores")]
public class AutoresController(LibraryDbContext db) : ControllerBase
{
    private static AutorDto ToDto(Autor a) => new(
        a.Id,
        a.Nombre,
        a.Nacionalidad,
        a.FechaNacimiento,
        a.Libros.Count
    );

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? nombre,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.Autores.AsNoTracking().Include(x => x.Libros).AsQueryable();

        if (!string.IsNullOrWhiteSpace(nombre))
            q = q.Where(x => x.Nombre.Contains(nombre.Trim()));

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.Nombre)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return Ok(new PagedResult<AutorDto>(items, total, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
    {
        var autor = await db.Autores.AsNoTracking()
            .Include(x => x.Libros)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        return autor is null ? NotFound(new { message = "Autor no encontrado." }) : Ok(ToDto(autor));
    }

    [HttpGet("{id:int}/libros")]
    public async Task<IActionResult> LibrosPorAutor(int id, CancellationToken ct)
    {
        if (!await db.Autores.AnyAsync(x => x.Id == id, ct))
            return NotFound(new { message = "Autor no encontrado." });

        var libros = await db.Libros.AsNoTracking()
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .Where(x => x.AutorId == id)
            .OrderBy(x => x.Titulo)
            .Select(b => new LibroDto(
                b.Id,
                b.Titulo,
                b.AnioPublicacion,
                b.Isbn,
                b.Disponible,
                b.AutorId,
                b.Autor!.Nombre,
                b.CategoriaId,
                b.Categoria!.Nombre,
                b.PortadaUrl,
                b.RowVersion
            ))
            .ToListAsync(ct);

        return Ok(libros);
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearAutorDto input, CancellationToken ct)
    {
        var autor = new Autor
        {
            Nombre = input.Nombre.Trim(),
            Nacionalidad = string.IsNullOrWhiteSpace(input.Nacionalidad) ? null : input.Nacionalidad.Trim(),
            FechaNacimiento = input.FechaNacimiento
        };

        db.Autores.Add(autor);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Obtener), new { id = autor.Id }, ToDto(autor));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarAutorDto input, CancellationToken ct)
    {
        var autor = await db.Autores.Include(x => x.Libros).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (autor is null) return NotFound(new { message = "Autor no encontrado." });

        autor.Nombre = input.Nombre.Trim();
        autor.Nacionalidad = string.IsNullOrWhiteSpace(input.Nacionalidad) ? null : input.Nacionalidad.Trim();
        autor.FechaNacimiento = input.FechaNacimiento;

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(autor));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPatch("{id:int}")]
    public async Task<IActionResult> ModificarParcial(int id, [FromBody] PatchAutorDto input, CancellationToken ct)
    {
        var autor = await db.Autores.Include(x => x.Libros).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (autor is null) return NotFound(new { message = "Autor no encontrado." });

        if (!string.IsNullOrWhiteSpace(input.Nombre))
            autor.Nombre = input.Nombre.Trim();

        if (input.Nacionalidad is not null)
            autor.Nacionalidad = string.IsNullOrWhiteSpace(input.Nacionalidad) ? null : input.Nacionalidad.Trim();

        if (input.FechaNacimiento.HasValue)
            autor.FechaNacimiento = input.FechaNacimiento;

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(autor));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var autor = await db.Autores.FindAsync([id], ct);
        if (autor is null) return NotFound(new { message = "Autor no encontrado." });

        if (await db.Libros.AnyAsync(x => x.AutorId == id, ct))
        {
            throw new DomainException(409, "No se puede eliminar el autor porque tiene libros asociados en el catálogo.");
        }

        db.Autores.Remove(autor);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
