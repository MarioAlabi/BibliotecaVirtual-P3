using System.Security.Claims;
using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Biblioteca.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Controllers;

[Authorize, ApiController, Route("api/lectores")]
public class LectoresController(LibraryDbContext db) : ControllerBase
{
    private static LectorDto ToDto(Lector l) => new(
        l.Id,
        l.Nombre,
        l.Correo,
        l.Telefono,
        l.EstaHabilitado,
        l.UserId,
        l.Prestamos.Count(p => p.Estado == "Activo")
    );

    private static PrestamoDto ToPrestamoDto(Prestamo p) => new(
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

    [Authorize(Roles = Roles.GestionLectores), HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? buscar,
        [FromQuery] bool? habilitado,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = db.Lectores.AsNoTracking().Include(x => x.Prestamos).AsQueryable();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim();
            q = q.Where(x => x.Nombre.Contains(b) || x.Correo.Contains(b) || (x.Telefono != null && x.Telefono.Contains(b)));
        }

        if (habilitado.HasValue)
            q = q.Where(x => x.EstaHabilitado == habilitado.Value);

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.Nombre)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return Ok(new PagedResult<LectorDto>(items, total, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
    {
        var lector = await db.Lectores.AsNoTracking()
            .Include(x => x.Prestamos)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (lector is null) return NotFound(new { message = "Lector no encontrado." });

        // Si es Lector, solo puede ver su propia información
        if (User.IsInRole(Roles.Lector) && !User.IsInRole(Roles.Bibliotecario) && !User.IsInRole(Roles.Administrador))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (lector.UserId != userId) return Forbid();
        }

        return Ok(ToDto(lector));
    }

    [Authorize(Roles = Roles.GestionLectores), HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearLectorDto input, CancellationToken ct)
    {
        if (await db.Lectores.AnyAsync(x => x.Correo == input.Correo.Trim(), ct))
            return BadRequest(new { message = "Ya existe un lector registrado con ese correo electrónico." });

        var lector = new Lector
        {
            Nombre = input.Nombre.Trim(),
            Correo = input.Correo.Trim(),
            Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim(),
            EstaHabilitado = input.EstaHabilitado
        };

        db.Lectores.Add(lector);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Obtener), new { id = lector.Id }, ToDto(lector));
    }

    [Authorize(Roles = Roles.GestionLectores), HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarLectorDto input, CancellationToken ct)
    {
        var lector = await db.Lectores.Include(x => x.Prestamos).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (lector is null) return NotFound(new { message = "Lector no encontrado." });

        var correo = input.Correo.Trim();
        if (await db.Lectores.AnyAsync(x => x.Correo == correo && x.Id != id, ct))
            return BadRequest(new { message = "Ya existe otro lector con ese correo electrónico." });

        lector.Nombre = input.Nombre.Trim();
        lector.Correo = correo;
        lector.Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim();
        lector.EstaHabilitado = input.EstaHabilitado;

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(lector));
    }

    [Authorize(Roles = Roles.GestionLectores), HttpPatch("{id:int}")]
    public async Task<IActionResult> ModificarParcial(int id, [FromBody] PatchLectorDto input, CancellationToken ct)
    {
        var lector = await db.Lectores.Include(x => x.Prestamos).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (lector is null) return NotFound(new { message = "Lector no encontrado." });

        if (!string.IsNullOrWhiteSpace(input.Nombre))
            lector.Nombre = input.Nombre.Trim();

        if (!string.IsNullOrWhiteSpace(input.Correo))
        {
            var correo = input.Correo.Trim();
            if (await db.Lectores.AnyAsync(x => x.Correo == correo && x.Id != id, ct))
                return BadRequest(new { message = "Ya existe otro lector con ese correo electrónico." });
            lector.Correo = correo;
        }

        if (input.Telefono is not null)
            lector.Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim();

        if (input.EstaHabilitado.HasValue)
            lector.EstaHabilitado = input.EstaHabilitado.Value;

        await db.SaveChangesAsync(ct);
        return Ok(ToDto(lector));
    }

    [Authorize(Roles = Roles.GestionLectores), HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var lector = await db.Lectores.FindAsync([id], ct);
        if (lector is null) return NotFound(new { message = "Lector no encontrado." });

        if (await db.Prestamos.AnyAsync(x => x.LectorId == id && x.Estado == "Activo", ct))
        {
            throw new DomainException(409, "No se puede eliminar un lector con préstamos activos.");
        }

        db.Lectores.Remove(lector);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:int}/prestamos")]
    public async Task<IActionResult> PrestamosLector(int id, CancellationToken ct)
    {
        var lector = await db.Lectores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (lector is null) return NotFound(new { message = "Lector no encontrado." });

        if (User.IsInRole(Roles.Lector) && !User.IsInRole(Roles.Bibliotecario) && !User.IsInRole(Roles.Administrador))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (lector.UserId != userId) return Forbid();
        }

        var prestamos = await db.Prestamos.AsNoTracking()
            .Include(x => x.Libro)
            .Include(x => x.Lector)
            .Where(x => x.LectorId == id)
            .OrderByDescending(x => x.FechaPrestamo)
            .Select(x => ToPrestamoDto(x))
            .ToListAsync(ct);

        return Ok(prestamos);
    }

    [HttpGet("{id:int}/prestamos/activos")]
    public async Task<IActionResult> PrestamosActivosLector(int id, CancellationToken ct)
    {
        var lector = await db.Lectores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (lector is null) return NotFound(new { message = "Lector no encontrado." });

        if (User.IsInRole(Roles.Lector) && !User.IsInRole(Roles.Bibliotecario) && !User.IsInRole(Roles.Administrador))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (lector.UserId != userId) return Forbid();
        }

        var prestamos = await db.Prestamos.AsNoTracking()
            .Include(x => x.Libro)
            .Include(x => x.Lector)
            .Where(x => x.LectorId == id && x.Estado == "Activo")
            .OrderByDescending(x => x.FechaPrestamo)
            .Select(x => ToPrestamoDto(x))
            .ToListAsync(ct);

        return Ok(prestamos);
    }
}
