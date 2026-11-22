using Microsoft.EntityFrameworkCore;

namespace AWSAppContainers;

// --- Modelo normalizado (3FN): Categoria 1 --- N Producto ---

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public List<Producto> Productos { get; set; } = new();
}

public class Producto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    public int CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
}

// --- Usuarios del sistema (login, registro y CRUD) ---

public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}

// DTOs de entrada (para no exponer las entidades en los POST)
public record CategoriaDto(string Nombre);
public record ProductoDto(string Nombre, decimal Precio, int CategoriaId);
public record RegistroDto(string Nombre, string Email, string Password);
public record LoginDto(string Email, string Password);
// En PUT la contraseña es opcional: si viene vacía se conserva la actual.
public record UsuarioDto(string Nombre, string Email, string? Password);

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    /// <summary>
    /// EnsureCreated no agrega tablas a una BD que ya existe (la de EC2 vive en un volumen),
    /// así que la tabla de usuarios se crea aparte si falta.
    /// </summary>
    public void EnsureUsuariosTable() => Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "Usuarios" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Usuarios" PRIMARY KEY AUTOINCREMENT,
            "Nombre" TEXT NOT NULL,
            "Email" TEXT NOT NULL,
            "PasswordHash" TEXT NOT NULL,
            "CreadoEn" TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Usuarios_Email" ON "Usuarios" ("Email");
        """);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Categoria>(e =>
        {
            e.Property(c => c.Nombre).IsRequired().HasMaxLength(100);
            e.HasIndex(c => c.Nombre).IsUnique();          // evita categorias duplicadas
        });

        b.Entity<Producto>(e =>
        {
            e.Property(p => p.Nombre).IsRequired().HasMaxLength(150);
            e.Property(p => p.Precio).HasColumnType("decimal(10,2)");
            e.HasOne(p => p.Categoria)                      // llave foranea
             .WithMany(c => c.Productos)
             .HasForeignKey(p => p.CategoriaId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Usuario>(e =>
        {
            e.Property(u => u.Nombre).IsRequired().HasMaxLength(100);
            e.Property(u => u.Email).IsRequired().HasMaxLength(150);
            e.HasIndex(u => u.Email).IsUnique();             // un correo por cuenta
            // SQLite no guarda la zona horaria: se marca como UTC al leer.
            e.Property(u => u.CreadoEn).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });
    }
}
