using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Biblioteca.Api.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(150)]
    public string? NombreCompleto { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Autor
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Nacionalidad { get; set; }

    public DateOnly? FechaNacimiento { get; set; }

    public ICollection<Libro> Libros { get; set; } = [];
}

public class Categoria
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Descripcion { get; set; }

    public ICollection<Libro> Libros { get; set; } = [];
}

public class Libro
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Titulo { get; set; } = string.Empty;

    public int AnioPublicacion { get; set; }

    [MaxLength(30)]
    public string? Isbn { get; set; }

    public bool Disponible { get; set; } = true;

    public int AutorId { get; set; }
    public Autor? Autor { get; set; }

    public int CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }

    [MaxLength(300)]
    public string? PortadaUrl { get; set; }

    [MaxLength(32)]
    public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");

    public ICollection<Prestamo> Prestamos { get; set; } = [];
}

public class Lector
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Correo { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Telefono { get; set; }

    public bool EstaHabilitado { get; set; } = true;

    [MaxLength(100)]
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public ICollection<Prestamo> Prestamos { get; set; } = [];
}

public class Prestamo
{
    public int Id { get; set; }

    public int LibroId { get; set; }
    public Libro Libro { get; set; } = null!;

    public int LectorId { get; set; }
    public Lector Lector { get; set; } = null!;

    public DateTime FechaPrestamo { get; set; }

    public DateTime FechaLimite { get; set; }

    public DateTime? FechaDevolucion { get; set; }

    [MaxLength(20)]
    public string Estado { get; set; } = "Activo"; // Activo, Devuelto, Cancelado

    [MaxLength(500)]
    public string? Observaciones { get; set; }

    // Columna calculada exclusiva para MariaDB: evita más de 1 préstamo activo por libro
    public int? ActiveBookId { get; private set; }
}
