using System.Security.Claims;
using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Biblioteca.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    LibraryDbContext db,
    JwtService jwt) : ControllerBase
{
    [HttpPost("registro"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Registro([FromBody] RegistroDto input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existingUser = await users.FindByEmailAsync(input.Email.Trim());
        if (existingUser is not null)
        {
            return BadRequest(new { message = "El correo electrónico ya está registrado." });
        }

        var user = new ApplicationUser
        {
            UserName = input.Email.Trim(),
            Email = input.Email.Trim(),
            NombreCompleto = input.Nombre.Trim()
        };

        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(" ", result.Errors.Select(e => e.Description));
            return BadRequest(new { message = $"No se pudo crear la cuenta: {errors}" });
        }

        var roleResult = await users.AddToRoleAsync(user, Roles.Lector);
        if (!roleResult.Succeeded)
        {
            throw new DomainException(503, "No se pudo asignar el rol de Lector al nuevo usuario.");
        }

        // Crear automáticamente la ficha en la tabla de Lectores
        var lector = new Lector
        {
            Nombre = input.Nombre.Trim(),
            Correo = input.Email.Trim(),
            Telefono = input.Telefono?.Trim(),
            EstaHabilitado = true,
            UserId = user.Id
        };
        db.Lectores.Add(lector);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        var token = jwt.GenerateToken(user, [Roles.Lector], lector.Id);
        return Ok(new AuthResponseDto(token, user.Email!, user.Id, [Roles.Lector], lector.Id, user.NombreCompleto));
    }

    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginDto input, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { message = "Credenciales incorrectas o cuenta inactiva." });
        }

        var valid = await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);
        if (!valid.Succeeded)
        {
            return Unauthorized(new { message = "Credenciales incorrectas o cuenta bloqueada temporalmente." });
        }

        var roles = await users.GetRolesAsync(user);
        var lector = await db.Lectores.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user.Id || x.Correo == user.Email, ct);

        var token = jwt.GenerateToken(user, roles, lector?.Id);
        return Ok(new AuthResponseDto(token, user.Email!, user.Id, [.. roles], lector?.Id, user.NombreCompleto));
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await users.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return Unauthorized();

        var roles = await users.GetRolesAsync(user);
        var lector = await db.Lectores.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user.Id || x.Correo == user.Email, ct);

        return Ok(new PerfilUsuarioDto(user.Id, user.Email!, user.NombreCompleto, [.. roles], lector?.Id));
    }
}
