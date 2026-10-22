using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Controllers;

[Authorize(Roles = Roles.Administrador), ApiController, Route("api/usuarios")]
public class UsuariosController(UserManager<ApplicationUser> users, LibraryDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = users.Users.AsNoTracking();
        var total = await q.CountAsync(ct);
        var userList = await q.OrderBy(x => x.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<UsuarioAdminDto>();
        foreach (var u in userList)
        {
            var r = await users.GetRolesAsync(u);
            items.Add(new UsuarioAdminDto(u.Id, u.Email!, u.NombreCompleto, u.IsActive, [.. r]));
        }

        return Ok(new PagedResult<UsuarioAdminDto>(items, total, page, pageSize));
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearUsuarioAdminDto input, CancellationToken ct)
    {
        if (!Roles.All.Contains(input.Role))
            return BadRequest(new { message = $"Rol inválido. Opciones: {string.Join(", ", Roles.All)}" });

        var user = new ApplicationUser
        {
            UserName = input.Email.Trim(),
            Email = input.Email.Trim(),
            NombreCompleto = input.NombreCompleto?.Trim()
        };

        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(" ", result.Errors.Select(e => e.Description));
            return BadRequest(new { message = errors });
        }

        await users.AddToRoleAsync(user, input.Role);

        // Si se crea con rol Lector, crear automáticamente su registro en Lectores
        if (input.Role == Roles.Lector)
        {
            var lector = new Lector
            {
                Nombre = input.NombreCompleto?.Trim() ?? input.Email.Trim(),
                Correo = input.Email.Trim(),
                EstaHabilitado = true,
                UserId = user.Id
            };
            db.Lectores.Add(lector);
            await db.SaveChangesAsync(ct);
        }

        return CreatedAtAction(nameof(Listar), new { id = user.Id },
            new UsuarioAdminDto(user.Id, user.Email!, user.NombreCompleto, user.IsActive, [input.Role]));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Actualizar(string id, [FromBody] ActualizarUsuarioAdminDto input, CancellationToken ct)
    {
        if (!Roles.All.Contains(input.Role))
            return BadRequest(new { message = $"Rol inválido. Opciones: {string.Join(", ", Roles.All)}" });

        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound(new { message = "Usuario no encontrado." });

        user.IsActive = input.IsActive;
        if (input.NombreCompleto is not null)
            user.NombreCompleto = input.NombreCompleto.Trim();

        await users.UpdateAsync(user);

        var currentRoles = await users.GetRolesAsync(user);
        await users.RemoveFromRolesAsync(user, currentRoles);
        await users.AddToRoleAsync(user, input.Role);
        await users.UpdateSecurityStampAsync(user);

        return Ok(new UsuarioAdminDto(user.Id, user.Email!, user.NombreCompleto, user.IsActive, [input.Role]));
    }
}
