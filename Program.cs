using AWSAppContainers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Ruta del archivo SQLite. En Docker se sobreescribe con la variable DB_PATH.
var dbPath = builder.Configuration["DB_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "data", "app.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddOpenApi();
builder.Services.AddHostedService<TcpServer>();   // socket TCP en el puerto 6061

// Un body inválido (JSON mal formado, vacío, tipos incorrectos) lanza una excepción
// para que el manejador de abajo responda con el mismo schema que el resto de la API.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

var app = builder.Build();

// Crea la BD y la siembra con datos de ejemplo al arrancar.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    if (!db.Categorias.Any())
    {
        var bebidas = new Categoria { Nombre = "Bebidas" };
        var snacks = new Categoria { Nombre = "Snacks" };
        db.Categorias.AddRange(bebidas, snacks);
        db.Productos.AddRange(
            new Producto { Nombre = "Café", Precio = 35.50m, Categoria = bebidas },
            new Producto { Nombre = "Té verde", Precio = 28.00m, Categoria = bebidas },
            new Producto { Nombre = "Galletas", Precio = 19.90m, Categoria = snacks });
        db.SaveChanges();
    }
}

// Cualquier error no controlado también responde { statusCode, data: [{ error }] }
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, mensaje) = ex switch
    {
        BadHttpRequestException { StatusCode: 415 } => (415, "El Content-Type debe ser application/json"),
        BadHttpRequestException b => (b.StatusCode, "La petición no es válida: revise el cuerpo JSON y los parámetros"),
        _ => (500, "Error interno del servidor"),
    };
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { statusCode = status, data = new[] { new { error = mensaje } } });
}));

// Errores que el framework responde sin cuerpo (ruta inexistente, método no permitido,
// Content-Type incorrecto) también salen con el schema { statusCode, data: [{ error }] }
app.UseStatusCodePages(async ctx =>
{
    var status = ctx.HttpContext.Response.StatusCode;
    var mensaje = status switch
    {
        404 => "Ruta no encontrada",
        405 => "Método HTTP no permitido para esta ruta",
        415 => "El Content-Type debe ser application/json",
        _ => "La petición no es válida",
    };
    await ctx.HttpContext.Response.WriteAsJsonAsync(new { statusCode = status, data = new[] { new { error = mensaje } } });
});

app.MapOpenApi();

// Envoltura del schema pedido: { statusCode: 200, data: [] }
static IResult Ok(object? data, int statusCode = 200) =>
    Results.Json(new { statusCode, data = data as System.Collections.IEnumerable ?? new[] { data } }, statusCode: statusCode);

static IResult Fail(string mensaje, int statusCode) =>
    Results.Json(new { statusCode, data = new[] { new { error = mensaje } } }, statusCode: statusCode);

// Validaciones compartidas por POST y PUT. Devuelven el mensaje de error o null si todo está bien.
static string? ValidarCategoria(CategoriaDto dto)
{
    if (string.IsNullOrWhiteSpace(dto.Nombre)) return "El nombre es obligatorio";
    if (dto.Nombre.Trim().Length > 100) return "El nombre no puede exceder 100 caracteres";
    return null;
}

static string? ValidarProducto(ProductoDto dto)
{
    if (string.IsNullOrWhiteSpace(dto.Nombre)) return "El nombre es obligatorio";
    if (dto.Nombre.Trim().Length > 150) return "El nombre no puede exceder 150 caracteres";
    if (dto.Precio < 0) return "El precio no puede ser negativo";
    if (dto.Precio > 99_999_999.99m) return "El precio excede el máximo permitido";
    return null;
}

// 1. Health check / raíz
app.MapGet("/", () => Ok(new { servicio = "Servicio de AWS API en EC2, revisando", estado = "ok" }));

// ---------------- CATEGORIAS ----------------

// 2. Listar categorias
app.MapGet("/api/categorias", async (AppDb db) =>
    Ok(await db.Categorias.Select(c => new { c.Id, c.Nombre }).ToListAsync()));

// 3. Obtener una categoria con sus productos
app.MapGet("/api/categorias/{id:int}", async (int id, AppDb db) =>
{
    var cat = await db.Categorias
        .Where(c => c.Id == id)
        .Select(c => new { c.Id, c.Nombre, Productos = c.Productos.Select(p => new { p.Id, p.Nombre, p.Precio }) })
        .FirstOrDefaultAsync();
    return cat is null ? Fail("Categoría no encontrada", 404) : Ok(cat);
});

// 4. Crear categoria
app.MapPost("/api/categorias", async (CategoriaDto dto, AppDb db) =>
{
    if (ValidarCategoria(dto) is { } error) return Fail(error, 400);

    var nombre = dto.Nombre.Trim();
    if (await db.Categorias.AnyAsync(c => c.Nombre == nombre)) return Fail("La categoría ya existe", 409);

    var cat = new Categoria { Nombre = nombre };
    db.Categorias.Add(cat);
    await db.SaveChangesAsync();
    return Ok(new { cat.Id, cat.Nombre }, 201);
});

// 5. Actualizar categoria
app.MapPut("/api/categorias/{id:int}", async (int id, CategoriaDto dto, AppDb db) =>
{
    if (ValidarCategoria(dto) is { } error) return Fail(error, 400);

    var cat = await db.Categorias.FindAsync(id);
    if (cat is null) return Fail("Categoría no encontrada", 404);

    var nombre = dto.Nombre.Trim();
    if (await db.Categorias.AnyAsync(c => c.Nombre == nombre && c.Id != id)) return Fail("La categoría ya existe", 409);

    cat.Nombre = nombre;
    await db.SaveChangesAsync();
    return Ok(new { cat.Id, cat.Nombre });
});

// 6. Eliminar categoria (borra en cascada sus productos)
app.MapDelete("/api/categorias/{id:int}", async (int id, AppDb db) =>
{
    var cat = await db.Categorias.FindAsync(id);
    if (cat is null) return Fail("Categoría no encontrada", 404);
    db.Categorias.Remove(cat);
    await db.SaveChangesAsync();
    return Ok(new { eliminada = id });
});

// ---------------- PRODUCTOS ----------------

// 7. Listar productos (opcionalmente filtrados por categoria)
app.MapGet("/api/productos", async (int? categoriaId, AppDb db) =>
{
    var q = db.Productos.AsQueryable();
    if (categoriaId is not null) q = q.Where(p => p.CategoriaId == categoriaId);
    return Ok(await q.Select(p => new { p.Id, p.Nombre, p.Precio, p.CategoriaId, Categoria = p.Categoria!.Nombre }).ToListAsync());
});

// 8. Obtener un producto
app.MapGet("/api/productos/{id:int}", async (int id, AppDb db) =>
{
    var prod = await db.Productos
        .Where(p => p.Id == id)
        .Select(p => new { p.Id, p.Nombre, p.Precio, p.CategoriaId, Categoria = p.Categoria!.Nombre })
        .FirstOrDefaultAsync();
    return prod is null ? Fail("Producto no encontrado", 404) : Ok(prod);
});

// 9. Crear producto
app.MapPost("/api/productos", async (ProductoDto dto, AppDb db) =>
{
    if (ValidarProducto(dto) is { } error) return Fail(error, 400);
    if (!await db.Categorias.AnyAsync(c => c.Id == dto.CategoriaId)) return Fail("La categoría no existe", 400);

    var prod = new Producto { Nombre = dto.Nombre.Trim(), Precio = dto.Precio, CategoriaId = dto.CategoriaId };
    db.Productos.Add(prod);
    await db.SaveChangesAsync();
    return Ok(new { prod.Id, prod.Nombre, prod.Precio, prod.CategoriaId }, 201);
});

// 10. Actualizar producto
app.MapPut("/api/productos/{id:int}", async (int id, ProductoDto dto, AppDb db) =>
{
    if (ValidarProducto(dto) is { } error) return Fail(error, 400);

    var prod = await db.Productos.FindAsync(id);
    if (prod is null) return Fail("Producto no encontrado", 404);
    if (!await db.Categorias.AnyAsync(c => c.Id == dto.CategoriaId)) return Fail("La categoría no existe", 400);

    prod.Nombre = dto.Nombre.Trim();
    prod.Precio = dto.Precio;
    prod.CategoriaId = dto.CategoriaId;
    await db.SaveChangesAsync();
    return Ok(new { prod.Id, prod.Nombre, prod.Precio, prod.CategoriaId });
});

// 11. Eliminar producto
app.MapDelete("/api/productos/{id:int}", async (int id, AppDb db) =>
{
    var prod = await db.Productos.FindAsync(id);
    if (prod is null) return Fail("Producto no encontrado", 404);
    db.Productos.Remove(prod);
    await db.SaveChangesAsync();
    return Ok(new { eliminado = id });
});

// ---------------- MANTENIMIENTO DE LA BD ----------------

// 12. Backup: genera una copia consistente del archivo .db y la descarga
app.MapGet("/api/db/backup", async (AppDb db) =>
{
    var destino = Path.Combine(Path.GetTempPath(), $"backup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");

    // VACUUM INTO crea una copia íntegra aunque la BD esté en uso (SQLite 3.27+)
    var sql = "VACUUM INTO '" + destino.Replace("'", "''") + "'";
    await db.Database.ExecuteSqlRawAsync(sql);

    var bytes = await File.ReadAllBytesAsync(destino);
    File.Delete(destino);
    return Results.File(bytes, "application/octet-stream", $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.db");
});

// 13. Vaciar: borra todos los registros y reinicia los autoincrementales
app.MapDelete("/api/db/vaciar", async (AppDb db) =>
{
    var productos = await db.Productos.ExecuteDeleteAsync();
    var categorias = await db.Categorias.ExecuteDeleteAsync();
    try { await db.Database.ExecuteSqlRawAsync("DELETE FROM sqlite_sequence"); } catch { /* esa tabla solo existe si ya hubo inserts */ }
    return Ok(new { productosEliminados = productos, categoriasEliminadas = categorias });
});

app.Run();

// Expone la clase Program para que el proyecto de pruebas pueda levantar la API en memoria.
public partial class Program;
