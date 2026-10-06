using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

// Las pruebas configuran la API con variables de entorno (proceso compartido), así que corren en serie.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AWSAppContainers.Tests;

/// <summary>
/// Base de todas las pruebas. xUnit crea una instancia nueva por cada prueba, así que cada una
/// levanta la API en memoria con su propia BD SQLite temporal, sembrada con los datos iniciales:
///   Categorías: 1 Bebidas, 2 Snacks
///   Productos:  1 Café (Bebidas), 2 Té verde (Bebidas), 3 Galletas (Snacks)
/// Ninguna prueba depende de otra ni toca la BD real.
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"awsapp-test-{Guid.NewGuid():N}.db");
    readonly WebApplicationFactory<Program> _factory;
    protected readonly HttpClient Client;

    protected ApiTestBase()
    {
        Environment.SetEnvironmentVariable("DB_PATH", _dbPath);
        Environment.SetEnvironmentVariable("TCP_PORT", "0");   // puerto libre aleatorio, para no chocar con el 6061
        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();   // suelta el archivo para poder borrarlo
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    /// <summary>
    /// Lee la respuesta y verifica el schema obligatorio { statusCode, data: [] }:
    /// statusCode del cuerpo igual al código HTTP y data siempre arreglo.
    /// </summary>
    protected static async Task<(HttpStatusCode Status, JsonElement Data)> Leer(HttpResponseMessage resp)
    {
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)resp.StatusCode, json.GetProperty("statusCode").GetInt32());
        var data = json.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        return (resp.StatusCode, data);
    }

    /// <summary>Mensaje de error de una respuesta fallida: data[0].error</summary>
    protected static string Error(JsonElement data) => data[0].GetProperty("error").GetString()!;

    /// <summary>Envía un body "crudo", para simular JSON mal formado o un Content-Type equivocado.</summary>
    protected Task<HttpResponseMessage> EnviarCrudo(HttpMethod metodo, string url, string body, string contentType = "application/json") =>
        Client.SendAsync(new HttpRequestMessage(metodo, url) { Content = new StringContent(body, Encoding.UTF8, contentType) });
}
