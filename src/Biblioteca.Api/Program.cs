using System.Text;
using System.Threading.RateLimiting;
using Biblioteca.Api.Data;
using Biblioteca.Api.Models;
using Biblioteca.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MySqlConnector;

// Cargar .env si existe en el directorio de trabajo o en la raíz de la solución
static void LoadDotEnv()
{
    var candidates = new[] { ".env", "../.env", "../../.env" };
    foreach (var path in candidates)
    {
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(parts[0].Trim())))
                {
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
                }
            }
            break;
        }
    }
}
LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

// Asegurar carpeta wwwroot y uploads para imágenes de portadas
var webRoot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "portadas"));
builder.Environment.WebRootPath = webRoot;

// Configurar conexión a la base de datos MariaDB
var connection = builder.Configuration.GetConnectionString("Library");
if (string.IsNullOrWhiteSpace(connection))
{
    var dbHost = Environment.GetEnvironmentVariable("DB_HOST") ?? builder.Configuration["DB_HOST"];
    if (!string.IsNullOrWhiteSpace(dbHost))
    {
        var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? builder.Configuration["DB_PORT"] ?? "3306";
        var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? builder.Configuration["DB_NAME"] ?? "biblioteca_virtual_p3";
        var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? builder.Configuration["DB_USER"] ?? "mvilleda";
        var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? builder.Configuration["DB_PASSWORD"] ?? "";
        connection = $"Server={dbHost};Port={dbPort};Database={dbName};User={dbUser};Password={dbPassword};SslMode=Preferred;";
    }
}

if (string.IsNullOrWhiteSpace(connection))
{
    throw new InvalidOperationException("Configura ConnectionStrings:Library o las variables DB_HOST, DB_NAME, DB_USER y DB_PASSWORD.");
}

builder.Services.AddDbContext<LibraryDbContext>(o =>
    o.UseMySql(connection, new MariaDbServerVersion(new Version(11, 4, 9))));

// Configurar Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 8;
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireDigit = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
})
.AddEntityFrameworkStores<LibraryDbContext>()
.AddDefaultTokenProviders();

// Configurar JWT Bearer Authentication
var jwtSecret = builder.Configuration["Jwt:Key"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? "super_secret_jwt_key_biblioteca_p3_2026_unicaes_secure!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BibliotecaApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "BibliotecaClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<LibraryService>();

// Configurar CORS
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5080", "http://127.0.0.1:5080"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// Rate limiter para endpoints de auth
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(
        c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Configurar Swagger / OpenAPI con soporte para JWT Bearer
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Biblioteca API REST · Periodo 3",
        Version = "v1",
        Description = "API REST completa para la gestión de Libros, Autores, Categorías, Lectores y Préstamos con .NET 10 y MariaDB.",
        Contact = new OpenApiContact { Name = "UNICAES - Desarrollo Multiplataforma" }
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingresa tu token JWT obtenido en /api/auth/login o /api/auth/registro."
    });

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

var app = builder.Build();

// CLI: Aplicar migraciones
if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Migraciones aplicadas correctamente.");
    return;
}

// CLI: Crear administrador inicial
if (args.Contains("--create-admin"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

    // Asegurar roles
    foreach (var role in Roles.All)
    {
        if (!await roleMgr.RoleExistsAsync(role))
        {
            await roleMgr.CreateAsync(new IdentityRole(role));
        }
    }

    var email = builder.Configuration["Bootstrap:AdminEmail"]
        ?? Environment.GetEnvironmentVariable("Bootstrap__AdminEmail")
        ?? "admin@biblioteca.test";
    var pass = builder.Configuration["Bootstrap:AdminPassword"]
        ?? Environment.GetEnvironmentVariable("Bootstrap__AdminPassword")
        ?? "Admin1234!";

    var existing = await userMgr.FindByEmailAsync(email);
    if (existing is null)
    {
        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            NombreCompleto = "Administrador del Sistema"
        };
        var res = await userMgr.CreateAsync(admin, pass);
        if (!res.Succeeded)
        {
            throw new InvalidOperationException($"Error al crear admin: {string.Join(" ", res.Errors.Select(e => e.Description))}");
        }
        await userMgr.AddToRoleAsync(admin, Roles.Administrador);
        app.Logger.LogInformation("Usuario administrador creado: {Email}", email);
    }
    else
    {
        if (!await userMgr.IsInRoleAsync(existing, Roles.Administrador))
        {
            await userMgr.AddToRoleAsync(existing, Roles.Administrador);
        }
    }
    return;
}

// CLI: Seed de datos de prueba completos
if (args.Contains("--seed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    // Asegurar roles
    foreach (var role in Roles.All)
    {
        if (!await roleMgr.RoleExistsAsync(role))
        {
            await roleMgr.CreateAsync(new IdentityRole(role));
        }
    }

    // Asegurar Autores
    if (!await db.Autores.AnyAsync())
    {
        var autores = new[]
        {
            new Autor { Nombre = "Gabriel García Márquez", Nacionalidad = "Colombiana", FechaNacimiento = new DateOnly(1927, 3, 6) },
            new Autor { Nombre = "Miguel de Cervantes", Nacionalidad = "Española", FechaNacimiento = new DateOnly(1547, 9, 29) },
            new Autor { Nombre = "Antoine de Saint-Exupéry", Nacionalidad = "Francesa", FechaNacimiento = new DateOnly(1900, 6, 29) },
            new Autor { Nombre = "Jane Austen", Nacionalidad = "Británica", FechaNacimiento = new DateOnly(1775, 12, 16) },
            new Autor { Nombre = "George Orwell", Nacionalidad = "Británica", FechaNacimiento = new DateOnly(1903, 6, 25) },
            new Autor { Nombre = "Carl Sagan", Nacionalidad = "Estadounidense", FechaNacimiento = new DateOnly(1934, 11, 9) },
            new Autor { Nombre = "Robert C. Martin", Nacionalidad = "Estadounidense", FechaNacimiento = new DateOnly(1952, 12, 5) },
            new Autor { Nombre = "Franz Kafka", Nacionalidad = "Austrohúngara", FechaNacimiento = new DateOnly(1883, 7, 3) }
        };
        db.Autores.AddRange(autores);
        await db.SaveChangesAsync();
    }

    // Asegurar Categorías
    if (!await db.Categorias.AnyAsync())
    {
        var categorias = new[]
        {
            new Categoria { Nombre = "Novela", Descripcion = "Obras narrativas de ficción extensa." },
            new Categoria { Nombre = "Clásicos", Descripcion = "Literatura universal fundamental y destacada." },
            new Categoria { Nombre = "Literatura", Descripcion = "Obras literarias de diversos géneros y épocas." },
            new Categoria { Nombre = "Distopía", Descripcion = "Ficción sobre sociedades deshumanizadas o totalitarias." },
            new Categoria { Nombre = "Ciencia", Descripcion = "Divulgación científica, física, astronomía y ciencias." },
            new Categoria { Nombre = "Tecnología", Descripcion = "Ingeniería de software, computación y programación." }
        };
        db.Categorias.AddRange(categorias);
        await db.SaveChangesAsync();
    }

    // Asegurar Libros
    if (!await db.Libros.AnyAsync())
    {
        var aGabo = await db.Autores.FirstAsync(x => x.Nombre.Contains("García Márquez"));
        var aCervantes = await db.Autores.FirstAsync(x => x.Nombre.Contains("Cervantes"));
        var aExupery = await db.Autores.FirstAsync(x => x.Nombre.Contains("Saint-Exupéry"));
        var aAusten = await db.Autores.FirstAsync(x => x.Nombre.Contains("Austen"));
        var aOrwell = await db.Autores.FirstAsync(x => x.Nombre.Contains("Orwell"));
        var aSagan = await db.Autores.FirstAsync(x => x.Nombre.Contains("Sagan"));
        var aMartin = await db.Autores.FirstAsync(x => x.Nombre.Contains("Martin"));
        var aKafka = await db.Autores.FirstAsync(x => x.Nombre.Contains("Kafka"));

        var cNovela = await db.Categorias.FirstAsync(x => x.Nombre == "Novela");
        var cClasicos = await db.Categorias.FirstAsync(x => x.Nombre == "Clásicos");
        var cLit = await db.Categorias.FirstAsync(x => x.Nombre == "Literatura");
        var cDistopia = await db.Categorias.FirstAsync(x => x.Nombre == "Distopía");
        var cCiencia = await db.Categorias.FirstAsync(x => x.Nombre == "Ciencia");
        var cTec = await db.Categorias.FirstAsync(x => x.Nombre == "Tecnología");

        var libros = new[]
        {
            new Libro { Titulo = "Cien años de soledad", AnioPublicacion = 1967, Isbn = "978-0307474728", AutorId = aGabo.Id, CategoriaId = cNovela.Id, Disponible = true },
            new Libro { Titulo = "Don Quijote de la Mancha", AnioPublicacion = 1605, Isbn = "978-8424116286", AutorId = aCervantes.Id, CategoriaId = cClasicos.Id, Disponible = true },
            new Libro { Titulo = "El principito", AnioPublicacion = 1943, Isbn = "978-0156013987", AutorId = aExupery.Id, CategoriaId = cLit.Id, Disponible = true },
            new Libro { Titulo = "Orgullo y prejuicio", AnioPublicacion = 1813, Isbn = "978-8491050070", AutorId = aAusten.Id, CategoriaId = cNovela.Id, Disponible = true },
            new Libro { Titulo = "1984", AnioPublicacion = 1949, Isbn = "978-0451524935", AutorId = aOrwell.Id, CategoriaId = cDistopia.Id, Disponible = true },
            new Libro { Titulo = "Cosmos", AnioPublicacion = 1980, Isbn = "978-0345331359", AutorId = aSagan.Id, CategoriaId = cCiencia.Id, Disponible = true },
            new Libro { Titulo = "Clean Code", AnioPublicacion = 2008, Isbn = "978-0132350884", AutorId = aMartin.Id, CategoriaId = cTec.Id, Disponible = true },
            new Libro { Titulo = "La metamorfosis", AnioPublicacion = 1915, Isbn = "978-8420651361", AutorId = aKafka.Id, CategoriaId = cLit.Id, Disponible = true }
        };
        db.Libros.AddRange(libros);
        await db.SaveChangesAsync();
    }

    // Asegurar Lectores iniciales
    if (!await db.Lectores.AnyAsync())
    {
        var lectores = new[]
        {
            new Lector { Nombre = "Mario Alabi", Correo = "mario.alabi@unicaes.edu.sv", Telefono = "7000-1111", EstaHabilitado = true },
            new Lector { Nombre = "Luis Felipe Balcáceres", Correo = "luis.balcaceres@unicaes.edu.sv", Telefono = "7000-2222", EstaHabilitado = true },
            new Lector { Nombre = "Sofía Ramos", Correo = "sofia.ramos@example.test", Telefono = "7000-3333", EstaHabilitado = true }
        };
        db.Lectores.AddRange(lectores);
        await db.SaveChangesAsync();
    }

    app.Logger.LogInformation("Seed completado exitosamente.");
    return;
}

// Middleware de seguridad y cabeceras
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    try
    {
        await next();
    }
    catch (Exception ex) when (ex is DomainException or DbUpdateConcurrencyException or DbUpdateException or MySqlException)
    {
        var mysql = ex as MySqlException ?? ex.InnerException as MySqlException;
        var status = ex is DomainException d
            ? d.Status
            : ex is DbUpdateConcurrencyException || mysql?.Number is 1062 or 1451 or 1213 or 1205
                ? 409
                : 503;

        var message = ex is DomainException domain
            ? domain.Message
            : status == 409
                ? "Los datos cambiaron en otra sesión o existe un conflicto de integridad. Recarga la información e intenta de nuevo."
                : "No fue posible completar la operación en la base de datos. Intenta de nuevo más tarde.";

        if (status == 503) app.Logger.LogError(ex, "Error de persistencia en MariaDB");
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { message });
    }
});

app.UseCors();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot),
    RequestPath = ""
});
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Biblioteca API v1");
    c.RoutePrefix = "swagger";
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (LibraryDbContext db) =>
{
    try
    {
        await db.Libros.AsNoTracking().Take(1).ToListAsync();
        return Results.Ok(new { status = "ready" });
    }
    catch
    {
        return Results.StatusCode(503);
    }
});

// Redirigir la raíz a Swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();
public partial class Program { }
