using Biblioteca.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Biblioteca.Api.Data;

public class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Autor> Autores => Set<Autor>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Libro> Libros => Set<Libro>();
    public DbSet<Lector> Lectores => Set<Lector>();
    public DbSet<Prestamo> Prestamos => Set<Prestamo>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Autores
        b.Entity<Autor>().HasIndex(x => x.Nombre);

        // Categorias
        b.Entity<Categoria>().HasIndex(x => x.Nombre);

        // Libros
        b.Entity<Libro>().Property(x => x.RowVersion).IsConcurrencyToken().HasMaxLength(32);
        b.Entity<Libro>().HasIndex(x => x.Titulo);
        b.Entity<Libro>().HasIndex(x => x.Disponible);
        b.Entity<Libro>()
            .HasOne(x => x.Autor)
            .WithMany(x => x.Libros)
            .HasForeignKey(x => x.AutorId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Libro>()
            .HasOne(x => x.Categoria)
            .WithMany(x => x.Libros)
            .HasForeignKey(x => x.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Lectores
        b.Entity<Lector>().HasIndex(x => x.Correo);
        b.Entity<Lector>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Prestamos
        b.Entity<Prestamo>()
            .HasOne(x => x.Libro)
            .WithMany(x => x.Prestamos)
            .HasForeignKey(x => x.LibroId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<Prestamo>()
            .HasOne(x => x.Lector)
            .WithMany(x => x.Prestamos)
            .HasForeignKey(x => x.LectorId)
            .OnDelete(DeleteBehavior.Restrict);

        // MariaDB computed column: solo los préstamos en estado 'Activo' compiten por la unicidad del libro
        b.Entity<Prestamo>()
            .Property(x => x.ActiveBookId)
            .HasComputedColumnSql("CASE WHEN `Estado` = 'Activo' THEN `LibroId` ELSE NULL END", stored: true);
        b.Entity<Prestamo>().HasIndex(x => x.ActiveBookId).IsUnique();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in ChangeTracker.Entries<Libro>().Where(e => e.State == EntityState.Modified))
        {
            e.Entity.RowVersion = Guid.NewGuid().ToString("N");
        }
        return base.SaveChangesAsync(ct);
    }
}
