using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace AWSAppContainers.Tests;

/// <summary>Health check, mantenimiento de la BD (backup y vaciar) y errores de ruta/método.</summary>
public class GeneralTests : ApiTestBase
{
    // ---------- GET / ----------

    [Fact]
    public async Task Raiz_DevuelveEstadoOk()
    {
        var (status, data) = await Leer(await Client.GetAsync("/"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("ok", data[0].GetProperty("estado").GetString());
    }

    // ---------- GET /api/db/backup ----------

    [Fact]
    public async Task Backup_DescargaUnArchivoSqliteValido()
    {
        var resp = await Client.GetAsync("/api/db/backup");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/octet-stream", resp.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".db", resp.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        // Todo archivo SQLite empieza con la cabecera "SQLite format 3\0"
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal("SQLite format 3\0", Encoding.ASCII.GetString(bytes, 0, 16));
    }

    [Fact]
    public async Task Backup_DosSeguidos_NoChocan()
    {
        var a = Client.GetAsync("/api/db/backup");
        var b = Client.GetAsync("/api/db/backup");

        Assert.Equal(HttpStatusCode.OK, (await a).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b).StatusCode);
    }

    // ---------- DELETE /api/db/vaciar ----------

    [Fact]
    public async Task Vaciar_BorraTodoYReportaCuantosRegistros()
    {
        var (status, data) = await Leer(await Client.DeleteAsync("/api/db/vaciar"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data[0].GetProperty("productosEliminados").GetInt32());
        Assert.Equal(2, data[0].GetProperty("categoriasEliminadas").GetInt32());

        var (_, productos) = await Leer(await Client.GetAsync("/api/productos"));
        var (_, categorias) = await Leer(await Client.GetAsync("/api/categorias"));
        Assert.Equal(0, productos.GetArrayLength());
        Assert.Equal(0, categorias.GetArrayLength());
    }

    [Fact]
    public async Task Vaciar_ReiniciaLosIds()
    {
        await Client.DeleteAsync("/api/db/vaciar");

        var (_, data) = await Leer(await Client.PostAsJsonAsync("/api/categorias", new { nombre = "Nueva" }));
        Assert.Equal(1, data[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Vaciar_SobreBdYaVacia_NoFalla()
    {
        await Client.DeleteAsync("/api/db/vaciar");
        var (status, data) = await Leer(await Client.DeleteAsync("/api/db/vaciar"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, data[0].GetProperty("productosEliminados").GetInt32());
    }

    [Fact]
    public async Task Vaciar_ConGet_NoBorraNada()
    {
        // Error del usuario: usar GET en vez de DELETE. No debe vaciar la BD por accidente.
        var (status, _) = await Leer(await Client.GetAsync("/api/db/vaciar"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);

        var (_, productos) = await Leer(await Client.GetAsync("/api/productos"));
        Assert.Equal(3, productos.GetArrayLength());
    }

    // ---------- Errores de ruta y método ----------

    [Fact]
    public async Task RutaInexistente_Devuelve404()
    {
        var (status, data) = await Leer(await Client.GetAsync("/api/clientes"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Ruta no encontrada", Error(data));
    }

    [Fact]
    public async Task MetodoNoSoportado_Devuelve405()
    {
        // PATCH no está implementado; el usuario debe usar PUT
        var (status, data) = await Leer(await Client.PatchAsync("/api/productos/1",
            new StringContent("{\"precio\":1}", Encoding.UTF8, "application/json")));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);
        Assert.Equal("Método HTTP no permitido para esta ruta", Error(data));
    }

    [Fact]
    public async Task DeleteSobreColeccion_Devuelve405()
    {
        // Error del usuario: olvidar el id en DELETE /api/productos/{id}
        var (status, _) = await Leer(await Client.DeleteAsync("/api/productos"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);
    }
}
