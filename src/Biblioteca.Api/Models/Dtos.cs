using System.ComponentModel.DataAnnotations;

namespace Biblioteca.Api.Models;

// ==================== AUTH DTOs ====================
public record LoginDto(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record RegistroDto(
    [Required, EmailAddress, MaxLength(200)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MaxLength(150)] string Nombre,
    [MaxLength(30)] string? Telefono
);

public record AuthResponseDto(
    string Token,
    string Email,
    string Id,
    string[] Roles,
    int? LectorId,
    string? Nombre
);

public record PerfilUsuarioDto(
    string Id,
    string Email,
    string? NombreCompleto,
    string[] Roles,
    int? LectorId
);

// ==================== AUTORES DTOs ====================
public record CrearAutorDto(
    [Required, MaxLength(150)] string Nombre,
    [MaxLength(100)] string? Nacionalidad,
    DateOnly? FechaNacimiento
);

public record ActualizarAutorDto(
    [Required, MaxLength(150)] string Nombre,
    [MaxLength(100)] string? Nacionalidad,
    DateOnly? FechaNacimiento
);

public record PatchAutorDto(
    [MaxLength(150)] string? Nombre,
    [MaxLength(100)] string? Nacionalidad,
    DateOnly? FechaNacimiento
);

public record AutorDto(
    int Id,
    string Nombre,
    string? Nacionalidad,
    DateOnly? FechaNacimiento,
    int TotalLibros
);

// ==================== CATEGORIAS DTOs ====================
public record CrearCategoriaDto(
    [Required, MaxLength(100)] string Nombre,
    [MaxLength(300)] string? Descripcion
);

public record ActualizarCategoriaDto(
    [Required, MaxLength(100)] string Nombre,
    [MaxLength(300)] string? Descripcion
);

public record PatchCategoriaDto(
    [MaxLength(100)] string? Nombre,
    [MaxLength(300)] string? Descripcion
);

public record CategoriaDto(
    int Id,
    string Nombre,
    string? Descripcion,
    int TotalLibros
);

// ==================== LIBROS DTOs ====================
public record CrearLibroDto(
    [Required, MaxLength(200)] string Titulo,
    [Range(1, 2100)] int AnioPublicacion,
    [MaxLength(30)] string? Isbn,
    [Range(1, int.MaxValue)] int AutorId,
    [Range(1, int.MaxValue)] int CategoriaId
);

public record ActualizarLibroDto(
    [Required, MaxLength(200)] string Titulo,
    [Range(1, 2100)] int AnioPublicacion,
    [MaxLength(30)] string? Isbn,
    [Range(1, int.MaxValue)] int AutorId,
    [Range(1, int.MaxValue)] int CategoriaId,
    [Required, RegularExpression("^[a-f0-9]{32}$")] string RowVersion
);

public record PatchLibroDto(
    [MaxLength(200)] string? Titulo,
    [Range(1, 2100)] int? AnioPublicacion,
    [MaxLength(30)] string? Isbn,
    [Range(1, int.MaxValue)] int? AutorId,
    [Range(1, int.MaxValue)] int? CategoriaId,
    bool? Disponible,
    [Required, RegularExpression("^[a-f0-9]{32}$")] string RowVersion
);

public record LibroDto(
    int Id,
    string Titulo,
    int AnioPublicacion,
    string? Isbn,
    bool Disponible,
    int AutorId,
    string AutorNombre,
    int CategoriaId,
    string CategoriaNombre,
    string? PortadaUrl,
    string RowVersion
);

public record VersionDto(
    [Required, RegularExpression("^[a-f0-9]{32}$")] string RowVersion
);

// ==================== LECTORES DTOs ====================
public record CrearLectorDto(
    [Required, MaxLength(150)] string Nombre,
    [Required, EmailAddress, MaxLength(200)] string Correo,
    [MaxLength(30)] string? Telefono,
    bool EstaHabilitado = true
);

public record ActualizarLectorDto(
    [Required, MaxLength(150)] string Nombre,
    [Required, EmailAddress, MaxLength(200)] string Correo,
    [MaxLength(30)] string? Telefono,
    bool EstaHabilitado = true
);

public record PatchLectorDto(
    [MaxLength(150)] string? Nombre,
    [EmailAddress, MaxLength(200)] string? Correo,
    [MaxLength(30)] string? Telefono,
    bool? EstaHabilitado
);

public record LectorDto(
    int Id,
    string Nombre,
    string Correo,
    string? Telefono,
    bool EstaHabilitado,
    string? UserId,
    int PrestamosActivos
);

// ==================== PRESTAMOS DTOs ====================
public record CrearPrestamoDto(
    [Range(1, int.MaxValue)] int LibroId,
    [Range(1, int.MaxValue)] int LectorId,
    [Range(1, 90)] int DiasPrestamo = 14,
    [MaxLength(500)] string? Observaciones = null,
    string? RowVersion = null,
    bool SimulateFailure = false
);

public record DevolucionPrestamoDto(
    [MaxLength(500)] string? Observaciones = null,
    string? RowVersion = null,
    bool SimulateFailure = false
);

public record CancelacionPrestamoDto(
    [MaxLength(500)] string? Motivo = null,
    string? RowVersion = null,
    bool SimulateFailure = false
);

public record PrestamoDto(
    int Id,
    int LibroId,
    string LibroTitulo,
    int LectorId,
    string LectorNombre,
    DateTime FechaPrestamo,
    DateTime FechaLimite,
    DateTime? FechaDevolucion,
    string Estado,
    string? Observaciones,
    string LibroRowVersion
);

// ==================== USUARIOS / ADMIN DTOs ====================
public record CrearUsuarioAdminDto(
    [Required, EmailAddress, MaxLength(200)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required] string Role,
    [MaxLength(150)] string? NombreCompleto
);

public record ActualizarUsuarioAdminDto(
    [Required] string Role,
    bool IsActive,
    [MaxLength(150)] string? NombreCompleto
);

public record UsuarioAdminDto(
    string Id,
    string Email,
    string? NombreCompleto,
    bool IsActive,
    string[] Roles
);

// ==================== PAGINACIÓN ====================
public record PagedResult<T>(
    List<T> Items,
    int Total,
    int Page,
    int PageSize
);
