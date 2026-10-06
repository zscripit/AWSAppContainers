using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AWSAppContainers;

// Servidor de sockets TCP que corre dentro de la misma app (y del mismo contenedor) que la API HTTP.
// Protocolo: un mensaje por línea, con el formato
//   {insert:<element>}  -> <element> es el mismo body del POST /api/productos
//                          ej. {insert:{"nombre":"Leche","precio":24.5,"categoriaId":1}}
//   {get:<element>}     -> <element> es el id del producto, solo o como JSON
//                          ej. {get:1}   o   {get:{"id":1}}
// La respuesta usa el mismo schema que la API: {"statusCode":200,"data":[...]}
public class TcpServer(IServiceScopeFactory scopes, IConfiguration config, ILogger<TcpServer> log) : BackgroundService
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // acentos legibles, igual que la API HTTP
    };

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var port = config.GetValue("TCP_PORT", 6061);
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        log.LogInformation("Servidor TCP escuchando en el puerto {Port}", port);

        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop);
                _ = AtenderAsync(client, stop);   // cada cliente en su propia tarea
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    async Task AtenderAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

            try
            {
                string? linea;
                while ((linea = await reader.ReadLineAsync(stop)) is not null)
                {
                    if (string.IsNullOrWhiteSpace(linea)) continue;
                    await writer.WriteLineAsync(JsonSerializer.Serialize(await ProcesarAsync(linea.Trim()), Json));
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { /* el cliente cerró */ }
        }
    }

    async Task<object> ProcesarAsync(string mensaje)
    {
        // Separa {comando:elemento} en comando y elemento
        var sep = mensaje.IndexOf(':');
        if (!mensaje.StartsWith('{') || !mensaje.EndsWith('}') || sep < 0)
            return Fail("Formato inválido. Use {insert:<element>} o {get:<element>}", 400);

        var comando = mensaje[1..sep].Trim().ToLowerInvariant();
        var elemento = mensaje[(sep + 1)..^1].Trim();

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();

        try
        {
            return comando switch
            {
                "insert" => await InsertAsync(elemento, db),
                "get" => await GetAsync(elemento, db),
                _ => Fail($"Comando desconocido '{comando}'. Use insert o get", 400),
            };
        }
        catch (JsonException)
        {
            return Fail("El elemento no es un JSON válido", 400);
        }
    }

    // Mismas reglas que POST /api/productos
    static async Task<object> InsertAsync(string elemento, AppDb db)
    {
        var dto = JsonSerializer.Deserialize<ProductoDto>(elemento, Json);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Nombre)) return Fail("El nombre es obligatorio", 400);
        if (dto.Precio < 0) return Fail("El precio no puede ser negativo", 400);
        if (!await db.Categorias.AnyAsync(c => c.Id == dto.CategoriaId)) return Fail("La categoría no existe", 400);

        var prod = new Producto { Nombre = dto.Nombre.Trim(), Precio = dto.Precio, CategoriaId = dto.CategoriaId };
        db.Productos.Add(prod);
        await db.SaveChangesAsync();
        return Ok(new { prod.Id, prod.Nombre, prod.Precio, prod.CategoriaId }, 201);
    }

    // Mismo resultado que GET /api/productos/{id}
    static async Task<object> GetAsync(string elemento, AppDb db)
    {
        int id;
        if (!int.TryParse(elemento, out id))
        {
            using var doc = JsonDocument.Parse(elemento);
            if (!doc.RootElement.TryGetProperty("id", out var p) || !p.TryGetInt32(out id))
                return Fail("Indique el id del producto: {get:1} o {get:{\"id\":1}}", 400);
        }

        var prod = await db.Productos
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Nombre, p.Precio, p.CategoriaId, Categoria = p.Categoria!.Nombre })
            .FirstOrDefaultAsync();
        return prod is null ? Fail("Producto no encontrado", 404) : Ok(prod);
    }

    static object Ok(object data, int statusCode = 200) => new { statusCode, data = new[] { data } };
    static object Fail(string error, int statusCode) => new { statusCode, data = new[] { new { error } } };
}
