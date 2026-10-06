using System.Net;
using System.Net.Http.Json;

namespace AWSAppContainers.Tests;

/// <summary>Endpoints de /api/categorias: GET, GET por id, POST, PUT y DELETE.</summary>
public class CategoriasTests : ApiTestBase
{
    // ---------- GET /api/categorias ----------

    [Fact]
    public async Task Listar_DevuelveLasCategoriasSembradas()
    {
        var (status, data) = await Leer(await Client.GetAsync("/api/categorias"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, data.GetArrayLength());
        Assert.Equal("Bebidas", data[0].GetProperty("nombre").GetString());
    }

    // ---------- GET /api/categorias/{id} ----------

    [Fact]
    public async Task ObtenerPorId_Existente_IncluyeSusProductos()
    {
        var (status, data) = await Leer(await Client.GetAsync("/api/categorias/1"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Bebidas", data[0].GetProperty("nombre").GetString());
        Assert.Equal(2, data[0].GetProperty("productos").GetArrayLength());
    }

    [Theory]
    [InlineData(999)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ObtenerPorId_Inexistente_Devuelve404(int id)
    {
        var (status, data) = await Leer(await Client.GetAsync($"/api/categorias/{id}"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Categoría no encontrada", Error(data));
    }

    [Fact]
    public async Task ObtenerPorId_IdNoNumerico_Devuelve404()
    {
        // Error del usuario: /api/categorias/abc no coincide con la ruta {id:int}
        var (status, data) = await Leer(await Client.GetAsync("/api/categorias/abc"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Ruta no encontrada", Error(data));
    }

    // ---------- POST /api/categorias ----------

    [Fact]
    public async Task Crear_Valida_Devuelve201YQuedaGuardada()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/categorias", new { nombre = "Lacteos" }));

        Assert.Equal(HttpStatusCode.Created, status);
        var id = data[0].GetProperty("id").GetInt32();
        Assert.Equal(3, id);

        var (_, guardada) = await Leer(await Client.GetAsync($"/api/categorias/{id}"));
        Assert.Equal("Lacteos", guardada[0].GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Crear_ConEspacios_GuardaElNombreRecortado()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/categorias", new { nombre = "  Lacteos  " }));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Lacteos", data[0].GetProperty("nombre").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Crear_NombreVacio_Devuelve400(string nombre)
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/categorias", new { nombre }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("El nombre es obligatorio", Error(data));
    }

    [Fact]
    public async Task Crear_SinCampoNombre_Devuelve400()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/categorias", new { }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("El nombre es obligatorio", Error(data));
    }

    [Fact]
    public async Task Crear_NombreMayorA100Caracteres_Devuelve400()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/categorias", new { nombre = new string('x', 101) }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("100 caracteres", Error(data));
    }

    [Fact]
    public async Task Crear_Duplicada_Devuelve409()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/categorias", new { nombre = "Bebidas" }));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("La categoría ya existe", Error(data));
    }

    [Fact]
    public async Task Crear_DuplicadaConEspacios_Devuelve409YNo500()
    {
        // Regresión: antes se comparaba sin recortar, pasaba la validación y la BD lanzaba un 500
        var (status, _) = await Leer(await Client.PostAsJsonAsync("/api/categorias", new { nombre = " Bebidas " }));

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Theory]
    [InlineData("{ \"nombre\": ")]          // JSON cortado
    [InlineData("{ nombre: 'Lacteos' }")]   // sin comillas dobles
    [InlineData("")]                        // body vacío
    [InlineData("null")]
    public async Task Crear_BodyInvalido_Devuelve400ConElSchema(string body)
    {
        var (status, data) = await Leer(await EnviarCrudo(HttpMethod.Post, "/api/categorias", body));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("no es válida", Error(data));
    }

    [Fact]
    public async Task Crear_ContentTypeIncorrecto_Devuelve415()
    {
        var (status, data) = await Leer(await EnviarCrudo(HttpMethod.Post, "/api/categorias", "nombre=Lacteos", "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.Contains("application/json", Error(data));
    }

    // ---------- PUT /api/categorias/{id} ----------

    [Fact]
    public async Task Actualizar_Valida_Devuelve200YPersisteElCambio()
    {
        var (status, data) = await Leer(await Client.PutAsJsonAsync("/api/categorias/2", new { nombre = "Botanas" }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Botanas", data[0].GetProperty("nombre").GetString());

        var (_, guardada) = await Leer(await Client.GetAsync("/api/categorias/2"));
        Assert.Equal("Botanas", guardada[0].GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Actualizar_ConSuMismoNombre_NoEsConflicto()
    {
        var (status, _) = await Leer(await Client.PutAsJsonAsync("/api/categorias/1", new { nombre = "Bebidas" }));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Actualizar_ConNombreDeOtraCategoria_Devuelve409()
    {
        var (status, data) = await Leer(await Client.PutAsJsonAsync("/api/categorias/2", new { nombre = "Bebidas" }));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("La categoría ya existe", Error(data));
    }

    [Fact]
    public async Task Actualizar_Inexistente_Devuelve404()
    {
        var (status, data) = await Leer(await Client.PutAsJsonAsync("/api/categorias/999", new { nombre = "Nueva" }));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Categoría no encontrada", Error(data));
    }

    [Fact]
    public async Task Actualizar_NombreVacio_Devuelve400()
    {
        var (status, _) = await Leer(await Client.PutAsJsonAsync("/api/categorias/1", new { nombre = "" }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // ---------- DELETE /api/categorias/{id} ----------

    [Fact]
    public async Task Eliminar_Existente_BorraTambienSusProductos()
    {
        var (status, data) = await Leer(await Client.DeleteAsync("/api/categorias/1"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data[0].GetProperty("eliminada").GetInt32());

        // Borrado en cascada: Café y Té verde eran de Bebidas, solo queda Galletas
        var (_, productos) = await Leer(await Client.GetAsync("/api/productos"));
        Assert.Equal(1, productos.GetArrayLength());
        Assert.Equal("Galletas", productos[0].GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Eliminar_Inexistente_Devuelve404()
    {
        var (status, data) = await Leer(await Client.DeleteAsync("/api/categorias/999"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Categoría no encontrada", Error(data));
    }

    [Fact]
    public async Task Eliminar_DosVeces_LaSegundaDevuelve404()
    {
        await Client.DeleteAsync("/api/categorias/2");
        var (status, _) = await Leer(await Client.DeleteAsync("/api/categorias/2"));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }
}
