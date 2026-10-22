namespace Biblioteca.Api.Models;

public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Bibliotecario = "Bibliotecario";
    public const string Lector = "Lector";

    public const string GestionCatalogo = Administrador + "," + Bibliotecario;
    public const string GestionLectores = Administrador + "," + Bibliotecario;
    public const string GestionPrestamos = Administrador + "," + Bibliotecario;
    public const string Todos = Administrador + "," + Bibliotecario + "," + Lector;

    public static readonly string[] All = [Administrador, Bibliotecario, Lector];
}
