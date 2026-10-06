using System.Net;
using System.Net.Http.Json;

namespace AWSAppContainers.Tests;

/// <summary>Endpoints de /api/productos: GET, GET por id, POST, PUT y DELETE.</summary>
public class ProductosTests : ApiTestBase
{
    // ---------- GET /api/productos ----------

    [Fact]
    public async Task Listar_DevuelveLosProductosConSuCategoria()
    {
        var (status, data) = await Leer(await Client.GetAsync("/api/productos"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, data.GetArrayLength());
        Assert.Equal("Café", data[0].GetProperty("nombre").GetString());
        Assert.Equal("Bebidas", data[0].GetProperty("categoria").GetString());
    }

    [Fact]
    public async Task Listar_FiltradoPorCategoria_SoloDevuelveLosDeEsaCategoria()
    {
        var (status, data) = await Leer(await Client.GetAsync("/api/productos?categoriaId=2"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetArrayLength());
        Assert.Equal("Galletas", data[0].GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Listar_FiltradoPorCategoriaInexistente_DevuelveArregloVacio()
    {
        var (status, data) = await Leer(await Client.GetAsync("/api/productos?categoriaId=999"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, data.GetArrayLength());
    }

    [Fact]
    public async Task Listar_FiltroNoNumerico_Devuelve400()
    {
        // Error del usuario: ?categoriaId=abc no se puede convertir a número
        var (status, _) = await Leer(await Client.GetAsync("/api/productos?categoriaId=abc"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // ---------- GET /api/productos/{id} ----------

    [Fact]
    public async Task ObtenerPorId_Existente_Devuelve200()
    {
        var (status, data) = await Leer(await Client.GetAsync("/api/productos/3"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Galletas", data[0].GetProperty("nombre").GetString());
        Assert.Equal(19.9m, data[0].GetProperty("precio").GetDecimal());
    }

    [Theory]
    [InlineData(999)]
    [InlineData(-5)]
    public async Task ObtenerPorId_Inexistente_Devuelve404(int id)
    {
        var (status, data) = await Leer(await Client.GetAsync($"/api/productos/{id}"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Producto no encontrado", Error(data));
    }

    // ---------- POST /api/productos ----------

    [Fact]
    public async Task Crear_Valido_Devuelve201YApareceEnLaCategoria()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/productos",
            new { nombre = "Agua", precio = 12.5m, categoriaId = 1 }));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(4, data[0].GetProperty("id").GetInt32());

        var (_, categoria) = await Leer(await Client.GetAsync("/api/categorias/1"));
        Assert.Equal(3, categoria[0].GetProperty("productos").GetArrayLength());
    }

    [Fact]
    public async Task Crear_PrecioCero_EsValido()
    {
        var (status, _) = await Leer(await Client.PostAsJsonAsync("/api/productos",
            new { nombre = "Muestra gratis", precio = 0m, categoriaId = 2 }));

        Assert.Equal(HttpStatusCode.Created, status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Crear_NombreVacio_Devuelve400(string nombre)
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/productos",
            new { nombre, precio = 10m, categoriaId = 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("El nombre es obligatorio", Error(data));
    }

    [Fact]
    public async Task Crear_PrecioNegativo_Devuelve400()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/productos",
            new { nombre = "Agua", precio = -1m, categoriaId = 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("El precio no puede ser negativo", Error(data));
    }

    [Fact]
    public async Task Crear_PrecioExcesivo_Devuelve400()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/productos",
            new { nombre = "Agua", precio = 1_000_000_000m, categoriaId = 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("El precio excede el máximo permitido", Error(data));
    }

    [Fact]
    public async Task Crear_NombreMayorA150Caracteres_Devuelve400()
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/productos",
            new { nombre = new string('x', 151), precio = 10m, categoriaId = 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("150 caracteres", Error(data));
    }

    [Theory]
    [InlineData(999)]
    [InlineData(0)]   // también es lo que llega si el usuario omite categoriaId
    public async Task Crear_CategoriaInexistente_Devuelve400(int categoriaId)
    {
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/productos",
            new { nombre = "Agua", precio = 10m, categoriaId }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("La categoría no existe", Error(data));
    }

    [Theory]
    [InlineData("{\"nombre\":\"Agua\",\"precio\":\"caro\",\"categoriaId\":1}")]   // precio como texto
    [InlineData("{\"nombre\":\"Agua\",\"precio\":10,\"categoriaId\":\"uno\"}")]   // id como texto
    [InlineData("{\"nombre\":\"Agua\",\"precio\":10,")]                           // JSON cortado
    [InlineData("[]")]                                                          // arreglo en lugar de objeto
    public async Task Crear_TiposIncorrectosOJsonInvalido_Devuelve400ConElSchema(string body)
    {
        var (status, data) = await Leer(await EnviarCrudo(HttpMethod.Post, "/api/productos", body));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("no es válida", Error(data));
    }

    // ---------- PUT /api/productos/{id} ----------

    [Fact]
    public async Task Actualizar_Valido_Devuelve200YPersisteElCambio()
    {
        var (status, data) = await Leer(await Client.PutAsJsonAsync("/api/productos/1",
            new { nombre = "Café americano", precio = 40m, categoriaId = 1 }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Café americano", data[0].GetProperty("nombre").GetString());

        var (_, guardado) = await Leer(await Client.GetAsync("/api/productos/1"));
        Assert.Equal(40m, guardado[0].GetProperty("precio").GetDecimal());
    }

    [Fact]
    public async Task Actualizar_CambiarDeCategoria_MueveElProducto()
    {
        await Client.PutAsJsonAsync("/api/productos/1", new { nombre = "Café", precio = 35.5m, categoriaId = 2 });

        var (_, snacks) = await Leer(await Client.GetAsync("/api/productos?categoriaId=2"));
        Assert.Equal(2, snacks.GetArrayLength());
    }

    [Fact]
    public async Task Actualizar_Inexistente_Devuelve404()
    {
        var (status, data) = await Leer(await Client.PutAsJsonAsync("/api/productos/999",
            new { nombre = "Agua", precio = 10m, categoriaId = 1 }));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Producto no encontrado", Error(data));
    }

    [Fact]
    public async Task Actualizar_CategoriaInexistente_Devuelve400YNoModifica()
    {
        var (status, _) = await Leer(await Client.PutAsJsonAsync("/api/productos/1",
            new { nombre = "Café", precio = 35.5m, categoriaId = 999 }));

        Assert.Equal(HttpStatusCode.BadRequest, status);

        var (_, original) = await Leer(await Client.GetAsync("/api/productos/1"));
        Assert.Equal(1, original[0].GetProperty("categoriaId").GetInt32());
    }

    [Fact]
    public async Task Actualizar_PrecioNegativo_Devuelve400()
    {
        var (status, _) = await Leer(await Client.PutAsJsonAsync("/api/productos/1",
            new { nombre = "Café", precio = -10m, categoriaId = 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // ---------- DELETE /api/productos/{id} ----------

    [Fact]
    public async Task Eliminar_Existente_YaNoSePuedeConsultar()
    {
        var (status, data) = await Leer(await Client.DeleteAsync("/api/productos/2"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, data[0].GetProperty("eliminado").GetInt32());

        var (despues, _) = await Leer(await Client.GetAsync("/api/productos/2"));
        Assert.Equal(HttpStatusCode.NotFound, despues);
    }

    [Fact]
    public async Task Eliminar_NoBorraSuCategoria()
    {
        await Client.DeleteAsync("/api/productos/3");

        var (status, _) = await Leer(await Client.GetAsync("/api/categorias/2"));
        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Eliminar_Inexistente_Devuelve404()
    {
        var (status, data) = await Leer(await Client.DeleteAsync("/api/productos/999"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Producto no encontrado", Error(data));
    }
}
