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

// DTOs de entrada (para no exponer las entidades en los POST)
public record CategoriaDto(string Nombre);
public record ProductoDto(string Nombre, decimal Precio, int CategoriaId);

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();

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
    }
}
