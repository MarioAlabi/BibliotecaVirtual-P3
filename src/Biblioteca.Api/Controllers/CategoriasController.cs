using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Biblioteca.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Controllers;

[ApiController, Route("api/categorias")]
public class CategoriasController(LibraryDbContext db) : ControllerBase
{
    private static CategoriaDto ToDto(Categoria c) => new(
        c.Id,
        c.Nombre,
        c.Descripcion,
        c.Libros.Count
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

        var q = db.Categorias.AsNoTracking().Include(x => x.Libros).AsQueryable();

        if (!string.IsNullOrWhiteSpace(nombre))
            q = q.Where(x => x.Nombre.Contains(nombre.Trim()));

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.Nombre)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return Ok(new PagedResult<CategoriaDto>(items, total, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
    {
        var cat = await db.Categorias.AsNoTracking()
            .Include(x => x.Libros)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        return cat is null ? NotFound(new { message = "Categoría no encontrada." }) : Ok(ToDto(cat));
    }

    [HttpGet("{id:int}/libros")]
    public async Task<IActionResult> LibrosPorCategoria(int id, CancellationToken ct)
    {
        if (!await db.Categorias.AnyAsync(x => x.Id == id, ct))
            return NotFound(new { message = "Categoría no encontrada." });

        var libros = await db.Libros.AsNoTracking()
            .Include(x => x.Autor)
            .Include(x => x.Categoria)
            .Where(x => x.CategoriaId == id)
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
    public async Task<IActionResult> Crear([FromBody] CrearCategoriaDto input, CancellationToken ct)
    {
        var cat = new Categoria
        {
            Nombre = input.Nombre.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(input.Descripcion) ? null : input.Descripcion.Trim()
        };

        db.Categorias.Add(cat);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Obtener), new { id = cat.Id }, ToDto(cat));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarCategoriaDto input, CancellationToken ct)
    {
        var cat = await db.Categorias.Include(x => x.Libros).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (cat is null) return NotFound(new { message = "Categoría no encontrada." });

        cat.Nombre = input.Nombre.Trim();
        cat.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion) ? null : input.Descripcion.Trim();

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(cat));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpPatch("{id:int}")]
    public async Task<IActionResult> ModificarParcial(int id, [FromBody] PatchCategoriaDto input, CancellationToken ct)
    {
        var cat = await db.Categorias.Include(x => x.Libros).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (cat is null) return NotFound(new { message = "Categoría no encontrada." });

        if (!string.IsNullOrWhiteSpace(input.Nombre))
            cat.Nombre = input.Nombre.Trim();

        if (input.Descripcion is not null)
            cat.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion) ? null : input.Descripcion.Trim();

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(cat));
    }

    [Authorize(Roles = Roles.GestionCatalogo), HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var cat = await db.Categorias.FindAsync([id], ct);
        if (cat is null) return NotFound(new { message = "Categoría no encontrada." });

        if (await db.Libros.AnyAsync(x => x.CategoriaId == id, ct))
        {
            throw new DomainException(409, "No se puede eliminar la categoría porque tiene libros asociados en el catálogo.");
        }

        db.Categorias.Remove(cat);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
