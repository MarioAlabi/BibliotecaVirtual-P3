using System.Security.Claims;
using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Biblioteca.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Controllers;

[Authorize, ApiController, Route("api/prestamos")]
public class PrestamosController(LibraryDbContext db, LibraryService library) : ControllerBase
{
    private static PrestamoDto ToDto(Prestamo p) => new(
        p.Id,
        p.LibroId,
        p.Libro?.Titulo ?? string.Empty,
        p.LectorId,
        p.Lector?.Nombre ?? string.Empty,
        p.FechaPrestamo,
        p.FechaLimite,
        p.FechaDevolucion,
        p.Estado,
        p.Observaciones,
        p.Libro?.RowVersion ?? string.Empty
    );

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? lectorId,
        [FromQuery] int? libroId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] string? estado,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.Prestamos.AsNoTracking()
            .Include(x => x.Libro)
            .Include(x => x.Lector)
            .AsQueryable();

        // Si es Lector, filtrar únicamente a sus propios préstamos
        if (User.IsInRole(Roles.Lector) && !User.IsInRole(Roles.Bibliotecario) && !User.IsInRole(Roles.Administrador))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            q = q.Where(x => x.Lector.UserId == userId);
        }
        else if (lectorId.HasValue && lectorId.Value > 0)
        {
            q = q.Where(x => x.LectorId == lectorId.Value);
        }

        if (libroId.HasValue && libroId.Value > 0)
            q = q.Where(x => x.LibroId == libroId.Value);

        if (desde.HasValue)
            q = q.Where(x => x.FechaPrestamo >= desde.Value);

        if (hasta.HasValue)
            q = q.Where(x => x.FechaPrestamo <= hasta.Value);

        if (!string.IsNullOrWhiteSpace(estado))
            q = q.Where(x => x.Estado == estado.Trim());

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.FechaPrestamo)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return Ok(new PagedResult<PrestamoDto>(items, total, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
    {
        var p = await db.Prestamos.AsNoTracking()
            .Include(x => x.Libro)
            .Include(x => x.Lector)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (p is null) return NotFound(new { message = "Préstamo no encontrado." });

        if (User.IsInRole(Roles.Lector) && !User.IsInRole(Roles.Bibliotecario) && !User.IsInRole(Roles.Administrador))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (p.Lector.UserId != userId) return Forbid();
        }

        return Ok(ToDto(p));
    }

    [HttpGet("activos")]
    public async Task<IActionResult> Activos(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return await Listar(null, null, null, null, "Activo", page, pageSize, ct);
    }

    [HttpGet("vencidos")]
    public async Task<IActionResult> Vencidos(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var now = DateTime.UtcNow;
        var q = db.Prestamos.AsNoTracking()
            .Include(x => x.Libro)
            .Include(x => x.Lector)
            .Where(x => x.Estado == "Activo" && x.FechaLimite < now);

        if (User.IsInRole(Roles.Lector) && !User.IsInRole(Roles.Bibliotecario) && !User.IsInRole(Roles.Administrador))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            q = q.Where(x => x.Lector.UserId == userId);
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.FechaLimite)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return Ok(new PagedResult<PrestamoDto>(items, total, page, pageSize));
    }

    [Authorize(Roles = Roles.GestionPrestamos), HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearPrestamoDto input, CancellationToken ct)
    {
        var prestamo = await library.Prestar(input, ct);

        var creado = await db.Prestamos.AsNoTracking()
            .Include(x => x.Libro)
            .Include(x => x.Lector)
            .SingleAsync(x => x.Id == prestamo.Id, ct);

        return CreatedAtAction(nameof(Obtener), new { id = prestamo.Id }, ToDto(creado));
    }

    [Authorize(Roles = Roles.GestionPrestamos), HttpPatch("{id:int}/devolucion")]
    public async Task<IActionResult> RegistrarDevolucion(int id, [FromBody] DevolucionPrestamoDto input, CancellationToken ct)
    {
        var prestamo = await library.Devolver(id, input, ct);

        var actualizado = await db.Prestamos.AsNoTracking()
            .Include(x => x.Libro)
            .Include(x => x.Lector)
            .SingleAsync(x => x.Id == prestamo.Id, ct);

        return Ok(ToDto(actualizado));
    }

    [Authorize(Roles = Roles.GestionPrestamos), HttpPatch("{id:int}/cancelacion")]
    public async Task<IActionResult> Cancelar(int id, [FromBody] CancelacionPrestamoDto input, CancellationToken ct)
    {
        var prestamo = await library.Cancelar(id, input, ct);

        var actualizado = await db.Prestamos.AsNoTracking()
            .Include(x => x.Libro)
            .Include(x => x.Lector)
            .SingleAsync(x => x.Id == prestamo.Id, ct);

        return Ok(ToDto(actualizado));
    }
}
