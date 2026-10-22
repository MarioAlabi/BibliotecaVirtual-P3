using System.Data;
using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Services;

public class DomainException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public class LibraryService(LibraryDbContext db, IWebHostEnvironment env, IConfiguration config)
{
    public static void CheckVersion(Libro libro, string? version)
    {
        if (version is not null && libro.RowVersion != version)
        {
            throw new DomainException(409, "Otra sesión modificó este libro. Recarga los datos antes de volver a intentarlo.");
        }
    }

    void ValidateSimulation(bool requested)
    {
        if (requested && !(env.IsDevelopment() && config.GetValue<bool>("Demo:EnableFailureSimulation")))
        {
            throw new DomainException(400, "La simulación de errores está deshabilitada.");
        }
    }

    public async Task<Prestamo> Prestar(CrearPrestamoDto input, CancellationToken ct = default)
    {
        ValidateSimulation(input.SimulateFailure);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var libro = await db.Libros.SingleOrDefaultAsync(x => x.Id == input.LibroId, ct)
                        ?? throw new DomainException(404, "Libro no encontrado.");

            CheckVersion(libro, input.RowVersion);

            if (!libro.Disponible)
            {
                throw new DomainException(409, "Este libro ya no se encuentra disponible para préstamo.");
            }

            var lector = await db.Lectores.SingleOrDefaultAsync(x => x.Id == input.LectorId, ct)
                         ?? throw new DomainException(404, "Lector no encontrado.");

            if (!lector.EstaHabilitado)
            {
                throw new DomainException(409, "El lector seleccionado no está habilitado para recibir préstamos.");
            }

            libro.Disponible = false;
            db.Entry(libro).State = EntityState.Modified;
            await db.SaveChangesAsync(ct);

            var now = DateTime.UtcNow;
            var prestamo = new Prestamo
            {
                LibroId = libro.Id,
                Libro = libro,
                LectorId = lector.Id,
                Lector = lector,
                FechaPrestamo = now,
                FechaLimite = now.AddDays(input.DiasPrestamo),
                Estado = "Activo",
                Observaciones = input.Observaciones?.Trim()
            };

            db.Prestamos.Add(prestamo);
            await db.SaveChangesAsync(ct);

            if (input.SimulateFailure)
            {
                throw new DomainException(422, "Fallo controlado: se revirtieron el préstamo y la disponibilidad (Rollback).");
            }

            await tx.CommitAsync(ct);
            return prestamo;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Prestamo> Devolver(int id, DevolucionPrestamoDto input, CancellationToken ct = default)
    {
        ValidateSimulation(input.SimulateFailure);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var prestamo = await db.Prestamos
                .Include(x => x.Libro)
                .Include(x => x.Lector)
                .SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new DomainException(404, "Préstamo no encontrado.");

            if (prestamo.Estado != "Activo")
            {
                throw new DomainException(409, $"Este préstamo ya no está activo (estado actual: {prestamo.Estado}).");
            }

            CheckVersion(prestamo.Libro, input.RowVersion);

            prestamo.Libro.Disponible = true;
            db.Entry(prestamo.Libro).State = EntityState.Modified;
            await db.SaveChangesAsync(ct);

            prestamo.FechaDevolucion = DateTime.UtcNow;
            prestamo.Estado = "Devuelto";
            if (!string.IsNullOrWhiteSpace(input.Observaciones))
            {
                prestamo.Observaciones = (prestamo.Observaciones + " | " + input.Observaciones).Trim(' ', '|');
            }

            await db.SaveChangesAsync(ct);

            if (input.SimulateFailure)
            {
                throw new DomainException(422, "Fallo controlado: se revirtió la devolución (Rollback).");
            }

            await tx.CommitAsync(ct);
            return prestamo;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Prestamo> Cancelar(int id, CancelacionPrestamoDto input, CancellationToken ct = default)
    {
        ValidateSimulation(input.SimulateFailure);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var prestamo = await db.Prestamos
                .Include(x => x.Libro)
                .Include(x => x.Lector)
                .SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new DomainException(404, "Préstamo no encontrado.");

            if (prestamo.Estado != "Activo")
            {
                throw new DomainException(409, $"No se puede cancelar un préstamo con estado '{prestamo.Estado}'.");
            }

            CheckVersion(prestamo.Libro, input.RowVersion);

            prestamo.Libro.Disponible = true;
            db.Entry(prestamo.Libro).State = EntityState.Modified;
            await db.SaveChangesAsync(ct);

            prestamo.Estado = "Cancelado";
            if (!string.IsNullOrWhiteSpace(input.Motivo))
            {
                prestamo.Observaciones = (prestamo.Observaciones + " | Cancelado: " + input.Motivo).Trim(' ', '|');
            }

            await db.SaveChangesAsync(ct);

            if (input.SimulateFailure)
            {
                throw new DomainException(422, "Fallo controlado: se revirtió la cancelación (Rollback).");
            }

            await tx.CommitAsync(ct);
            return prestamo;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
