using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AWSAppContainers.Tests;

/// <summary>Registro, login y CRUD de /api/usuarios (este último requiere token JWT).</summary>
public class UsuariosTests : ApiTestBase
{
    const string Password = "Prueba123";

    Task<HttpResponseMessage> Registrar(string nombre, string email, string password = Password) =>
        Client.PostAsJsonAsync("/api/auth/registro", new { nombre, email, password });

    /// <summary>Registra un usuario, inicia sesión y deja el token en el cliente. Devuelve su id.</summary>
    async Task<int> IniciarSesion(string email = "qa@example.com")
    {
        var (_, creado) = await Leer(await Registrar("QA Tester", email));
        var (_, login) = await Leer(await Client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }));
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login[0].GetProperty("token").GetString());
        return creado[0].GetProperty("id").GetInt32();
    }

    // ---------- Registro ----------

    [Fact]
    public async Task Registro_Valido_Devuelve201SinPassword()
    {
        var (status, data) = await Leer(await Registrar("Ana López", "Ana@Example.com"));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("ana@example.com", data[0].GetProperty("email").GetString());   // se normaliza
        Assert.False(data[0].TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task Registro_CorreoDuplicado_Devuelve409()
    {
        await Registrar("Ana", "ana@example.com");
        var (status, data) = await Leer(await Registrar("Otra Ana", "ANA@example.com"));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("El correo ya está registrado", Error(data));
    }

    [Theory]
    [InlineData("", "a@b.com", Password, "El nombre es obligatorio")]
    [InlineData("Ana", "", Password, "El correo es obligatorio")]
    [InlineData("Ana", "no-es-correo", Password, "El correo no tiene un formato válido")]
    [InlineData("Ana", "a@b.com", "", "La contraseña es obligatoria")]
    [InlineData("Ana", "a@b.com", "Ab1", "La contraseña debe tener al menos 8 caracteres")]
    [InlineData("Ana", "a@b.com", "solamenteletras", "La contraseña debe incluir letras y números")]
    [InlineData("Ana", "a@b.com", "1234567890", "La contraseña debe incluir letras y números")]
    public async Task Registro_DatosInvalidos_Devuelve400(string nombre, string email, string password, string mensaje)
    {
        var (status, data) = await Leer(await Registrar(nombre, email, password));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(mensaje, Error(data));
    }

    [Fact]
    public async Task Registro_NombreMuyLargo_Devuelve400()
    {
        var (status, data) = await Leer(await Registrar(new string('a', 101), "a@b.com"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("El nombre no puede exceder 100 caracteres", Error(data));
    }

    [Fact]
    public async Task Registro_CorreoMuyLargo_Devuelve400()
    {
        var (status, data) = await Leer(await Registrar("Ana", new string('a', 145) + "@b.com"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("El correo no puede exceder 150 caracteres", Error(data));
    }

    // ---------- Login ----------

    [Fact]
    public async Task Login_Correcto_DevuelveToken()
    {
        await Registrar("Ana", "ana@example.com");
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/auth/login",
            new { email = " ANA@example.com ", password = Password }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(string.IsNullOrEmpty(data[0].GetProperty("token").GetString()));
        Assert.Equal("ana@example.com", data[0].GetProperty("usuario").GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("ana@example.com", "Incorrecta1")]
    [InlineData("noexiste@example.com", Password)]
    public async Task Login_CredencialesIncorrectas_Devuelve401(string email, string password)
    {
        await Registrar("Ana", "ana@example.com");
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/auth/login", new { email, password }));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("Correo o contraseña incorrectos", Error(data));
    }

    [Fact]
    public async Task Login_CamposVacios_Devuelve400()
    {
        var (status, _) = await Leer(await Client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Yo_ConToken_DevuelveUsuarioActual()
    {
        var id = await IniciarSesion();
        var (status, data) = await Leer(await Client.GetAsync("/api/auth/yo"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(id, data[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Yo_UsuarioEliminado_Devuelve401()
    {
        var id = await IniciarSesion("borrado@example.com");
        // Otro usuario borra la cuenta mientras su token sigue vigente.
        var token = Client.DefaultRequestHeaders.Authorization;
        await IniciarSesion("admin@example.com");
        await Client.DeleteAsync($"/api/usuarios/{id}");
        Client.DefaultRequestHeaders.Authorization = token;

        var (status, _) = await Leer(await Client.GetAsync("/api/auth/yo"));
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    // ---------- Protección con JWT ----------

    [Theory]
    [InlineData("GET", "/api/usuarios")]
    [InlineData("GET", "/api/usuarios/1")]
    [InlineData("DELETE", "/api/usuarios/1")]
    [InlineData("GET", "/api/auth/yo")]
    public async Task SinToken_Devuelve401(string metodo, string url)
    {
        var (status, data) = await Leer(await Client.SendAsync(new HttpRequestMessage(new HttpMethod(metodo), url)));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("Debe iniciar sesión", Error(data));
    }

    [Fact]
    public async Task TokenInvalido_Devuelve401()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token.falso.123");
        var (status, _) = await Leer(await Client.GetAsync("/api/usuarios"));
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    // ---------- CRUD ----------

    [Fact]
    public async Task Listar_IncluyeUsuarioActual()
    {
        await IniciarSesion();
        var (status, data) = await Leer(await Client.GetAsync("/api/usuarios"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, data.GetArrayLength());
    }

    [Theory]
    [InlineData("luis", 1)]
    [InlineData("EXAMPLE.ORG", 2)]
    [InlineData("zzz", 0)]
    public async Task Listar_ConBusqueda_Filtra(string buscar, int esperados)
    {
        await IniciarSesion();
        await Client.PostAsJsonAsync("/api/usuarios", new { nombre = "Luis Pérez", email = "luis@example.org", password = Password });
        await Client.PostAsJsonAsync("/api/usuarios", new { nombre = "María", email = "maria@example.org", password = Password });

        var (_, data) = await Leer(await Client.GetAsync($"/api/usuarios?buscar={buscar}"));
        Assert.Equal(esperados, data.GetArrayLength());
    }

    [Fact]
    public async Task Crear_Valido_Devuelve201YPuedeIniciarSesion()
    {
        await IniciarSesion();
        var (status, data) = await Leer(await Client.PostAsJsonAsync("/api/usuarios",
            new { nombre = "Nuevo", email = "nuevo@example.com", password = "OtraClave9" }));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Nuevo", data[0].GetProperty("nombre").GetString());

        var (loginStatus, _) = await Leer(await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "nuevo@example.com", password = "OtraClave9" }));
        Assert.Equal(HttpStatusCode.OK, loginStatus);
    }

    [Fact]
    public async Task Crear_SinPassword_Devuelve400()
    {
        await IniciarSesion();
        var (status, _) = await Leer(await Client.PostAsJsonAsync("/api/usuarios",
            new { nombre = "Nuevo", email = "nuevo@example.com", password = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Crear_CorreoDuplicado_Devuelve409()
    {
        await IniciarSesion();
        var (status, _) = await Leer(await Client.PostAsJsonAsync("/api/usuarios",
            new { nombre = "Copia", email = "qa@example.com", password = Password }));
        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task ObtenerPorId_Existente_Y_Inexistente()
    {
        var id = await IniciarSesion();

        var (ok, data) = await Leer(await Client.GetAsync($"/api/usuarios/{id}"));
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal("qa@example.com", data[0].GetProperty("email").GetString());

        var (notFound, error) = await Leer(await Client.GetAsync("/api/usuarios/999"));
        Assert.Equal(HttpStatusCode.NotFound, notFound);
        Assert.Equal("Usuario no encontrado", Error(error));
    }

    [Fact]
    public async Task Actualizar_SinPassword_ConservaLaActual()
    {
        var id = await IniciarSesion();
        var (status, data) = await Leer(await Client.PutAsJsonAsync($"/api/usuarios/{id}",
            new { nombre = "Nombre Editado", email = "qa@example.com", password = "" }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Nombre Editado", data[0].GetProperty("nombre").GetString());
        var (login, _) = await Leer(await Client.PostAsJsonAsync("/api/auth/login", new { email = "qa@example.com", password = Password }));
        Assert.Equal(HttpStatusCode.OK, login);
    }

    [Fact]
    public async Task Actualizar_ConPassword_LaCambia()
    {
        var id = await IniciarSesion();
        await Client.PutAsJsonAsync($"/api/usuarios/{id}", new { nombre = "QA", email = "qa@example.com", password = "NuevaClave7" });

        var (vieja, _) = await Leer(await Client.PostAsJsonAsync("/api/auth/login", new { email = "qa@example.com", password = Password }));
        var (nueva, _) = await Leer(await Client.PostAsJsonAsync("/api/auth/login", new { email = "qa@example.com", password = "NuevaClave7" }));
        Assert.Equal(HttpStatusCode.Unauthorized, vieja);
        Assert.Equal(HttpStatusCode.OK, nueva);
    }

    [Fact]
    public async Task Actualizar_Inexistente_Devuelve404()
    {
        await IniciarSesion();
        var (status, _) = await Leer(await Client.PutAsJsonAsync("/api/usuarios/999",
            new { nombre = "X", email = "x@example.com", password = "" }));
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Actualizar_CorreoDeOtro_Devuelve409()
    {
        var id = await IniciarSesion();
        await Client.PostAsJsonAsync("/api/usuarios", new { nombre = "Otro", email = "otro@example.com", password = Password });

        var (status, _) = await Leer(await Client.PutAsJsonAsync($"/api/usuarios/{id}",
            new { nombre = "QA", email = "otro@example.com", password = "" }));
        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task Actualizar_DatosInvalidos_Devuelve400()
    {
        var id = await IniciarSesion();
        var (status, _) = await Leer(await Client.PutAsJsonAsync($"/api/usuarios/{id}",
            new { nombre = "", email = "qa@example.com", password = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Eliminar_OtroUsuario_LoBorra()
    {
        await IniciarSesion();
        var (_, creado) = await Leer(await Client.PostAsJsonAsync("/api/usuarios",
            new { nombre = "Temporal", email = "temp@example.com", password = Password }));
        var id = creado[0].GetProperty("id").GetInt32();

        var (status, _) = await Leer(await Client.DeleteAsync($"/api/usuarios/{id}"));
        Assert.Equal(HttpStatusCode.OK, status);

        var (despues, _) = await Leer(await Client.GetAsync($"/api/usuarios/{id}"));
        Assert.Equal(HttpStatusCode.NotFound, despues);
    }

    [Fact]
    public async Task Eliminar_PropiaCuenta_Devuelve400()
    {
        var id = await IniciarSesion();
        var (status, data) = await Leer(await Client.DeleteAsync($"/api/usuarios/{id}"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("No puede eliminar su propia cuenta", Error(data));
    }

    [Fact]
    public async Task Eliminar_Inexistente_Devuelve404()
    {
        await IniciarSesion();
        var (status, _) = await Leer(await Client.DeleteAsync("/api/usuarios/999"));
        Assert.Equal(HttpStatusCode.NotFound, status);
    }
}
