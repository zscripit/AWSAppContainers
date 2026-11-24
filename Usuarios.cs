using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AWSAppContainers;

/// <summary>Respuestas con el schema de la API: { statusCode, data: [] }.</summary>
public static class Api
{
    public static IResult Ok(object? data, int statusCode = 200) =>
        Results.Json(new { statusCode, data = data as System.Collections.IEnumerable ?? new[] { data } }, statusCode: statusCode);

    public static IResult Fail(string mensaje, int statusCode) =>
        Results.Json(new { statusCode, data = new[] { new { error = mensaje } } }, statusCode: statusCode);
}

/// <summary>Hash de contraseñas con PBKDF2 (SHA-256, sal aleatoria). Formato: iteraciones.sal.hash</summary>
public static class Passwords
{
    const int Iteraciones = 100_000;

    public static string Hash(string password)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, sal, Iteraciones, HashAlgorithmName.SHA256, 32);
        return $"{Iteraciones}.{Convert.ToBase64String(sal)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var partes = stored.Split('.');
        if (partes.Length != 3 || !int.TryParse(partes[0], out var iteraciones)) return false;
        var sal = Convert.FromBase64String(partes[1]);
        var esperado = Convert.FromBase64String(partes[2]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(hash, esperado);
    }
}

/// <summary>Configuración del token JWT. La llave viene de JWT_KEY; sin ella se genera una al arrancar.</summary>
public class JwtSettings
{
    public const string Issuer = "AWSAppContainers";
    public SymmetricSecurityKey Key { get; }
    public TimeSpan Duracion { get; } = TimeSpan.FromHours(8);

    public JwtSettings(string? key)
    {
        // Una llave aleatoria invalida las sesiones al reiniciar, pero nunca deja una llave fija en el código.
        var bytes = string.IsNullOrWhiteSpace(key) ? RandomNumberGenerator.GetBytes(32) : Encoding.UTF8.GetBytes(key);
        if (bytes.Length < 32) bytes = SHA256.HashData(bytes);
        Key = new SymmetricSecurityKey(bytes);
    }

    public string CrearToken(Usuario u, out DateTime expira)
    {
        expira = DateTime.UtcNow.Add(Duracion);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, u.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, u.Email),
                     new Claim("nombre", u.Nombre)],
            expires: expira,
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static partial class UsuariosEndpoints
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    static object Publico(Usuario u) => new { u.Id, u.Nombre, u.Email, u.CreadoEn };

    static string? Validar(string? nombre, string? email, string? password, bool passwordRequerido)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "El nombre es obligatorio";
        if (nombre.Trim().Length > 100) return "El nombre no puede exceder 100 caracteres";
        if (string.IsNullOrWhiteSpace(email)) return "El correo es obligatorio";
        if (email.Trim().Length > 150) return "El correo no puede exceder 150 caracteres";
        if (!EmailRegex().IsMatch(email.Trim())) return "El correo no tiene un formato válido";
        if (string.IsNullOrEmpty(password)) return passwordRequerido ? "La contraseña es obligatoria" : null;
        if (password.Length < 8) return "La contraseña debe tener al menos 8 caracteres"; // Cambiar '< 8' por '< 4'
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "La contraseña debe incluir letras y números";
        return null;
    }

    static string Normalizar(string email) => email.Trim().ToLowerInvariant();

    static int? UsuarioActual(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static void MapUsuarios(this WebApplication app)
    {
        var logger = app.Logger;

        // ---------- Autenticación ----------
        app.MapPost("/api/auth/registro", async (RegistroDto dto, AppDb db) =>
        {
            if (Validar(dto.Nombre, dto.Email, dto.Password, true) is { } error) return Api.Fail(error, 400);
            var email = Normalizar(dto.Email);
            if (await db.Usuarios.AnyAsync(u => u.Email == email)) return Api.Fail("El correo ya está registrado", 409);

            var u = new Usuario { Nombre = dto.Nombre.Trim(), Email = email, PasswordHash = Passwords.Hash(dto.Password) };
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            logger.LogInformation("Usuario registrado {UsuarioId} {Email}", u.Id, u.Email);
            return Api.Ok(Publico(u), 201);
        });

        app.MapPost("/api/auth/login", async (LoginDto dto, AppDb db, JwtSettings jwt) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrEmpty(dto.Password))
                return Api.Fail("El correo y la contraseña son obligatorios", 400);

            var email = Normalizar(dto.Email);
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email);
            if (u is null || !Passwords.Verify(dto.Password, u.PasswordHash))
            {
                logger.LogWarning("Inicio de sesión fallido {Email}", email);
                return Api.Fail("Correo o contraseña incorrectos", 401); // Cambiar el 401 por 400
            }

            var token = jwt.CrearToken(u, out var expira);
            logger.LogInformation("Inicio de sesión correcto {UsuarioId}", u.Id);
            return Api.Ok(new { token, expira, usuario = Publico(u) });
        });

        app.MapGet("/api/auth/yo", async (ClaimsPrincipal user, AppDb db) =>
        {
            var u = UsuarioActual(user) is { } id ? await db.Usuarios.FindAsync(id) : null;
            return u is null ? Api.Fail("La sesión ya no es válida", 401) : Api.Ok(Publico(u));
        }).RequireAuthorization();

        // ---------- CRUD de usuarios (requiere sesión) ----------
        var usuarios = app.MapGroup("/api/usuarios").RequireAuthorization();

        usuarios.MapGet("/", async (string? buscar, AppDb db) =>
        {
            var q = db.Usuarios.AsQueryable();
            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var t = buscar.Trim().ToLower();
                q = q.Where(u => u.Nombre.ToLower().Contains(t) || u.Email.Contains(t));
            }
            return Api.Ok(await q.OrderBy(u => u.Id).Select(u => new { u.Id, u.Nombre, u.Email, u.CreadoEn }).ToListAsync());
        });

        usuarios.MapGet("/{id:int}", async (int id, AppDb db) =>
            await db.Usuarios.FindAsync(id) is { } u ? Api.Ok(Publico(u)) : Api.Fail("Usuario no encontrado", 404));

        usuarios.MapPost("/", async (UsuarioDto dto, AppDb db) =>
        {
            if (Validar(dto.Nombre, dto.Email, dto.Password, true) is { } error) return Api.Fail(error, 400);
            var email = Normalizar(dto.Email);
            if (await db.Usuarios.AnyAsync(u => u.Email == email)) return Api.Fail("El correo ya está registrado", 409);

            var u = new Usuario { Nombre = dto.Nombre.Trim(), Email = email, PasswordHash = Passwords.Hash(dto.Password!) };
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            logger.LogInformation("Usuario creado {UsuarioId}", u.Id);
            return Api.Ok(Publico(u), 201);
        });

        usuarios.MapPut("/{id:int}", async (int id, UsuarioDto dto, AppDb db) =>
        {
            if (Validar(dto.Nombre, dto.Email, dto.Password, false) is { } error) return Api.Fail(error, 400);
            var u = await db.Usuarios.FindAsync(id);
            if (u is null) return Api.Fail("Usuario no encontrado", 404);

            var email = Normalizar(dto.Email);
            if (await db.Usuarios.AnyAsync(x => x.Email == email && x.Id != id)) return Api.Fail("El correo ya está registrado", 409);

            u.Nombre = dto.Nombre.Trim();
            u.Email = email;
            if (!string.IsNullOrEmpty(dto.Password)) u.PasswordHash = Passwords.Hash(dto.Password);
            await db.SaveChangesAsync();
            logger.LogInformation("Usuario actualizado {UsuarioId}", u.Id);
            return Api.Ok(Publico(u));
        });

        usuarios.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, AppDb db) =>
        {
            if (UsuarioActual(user) == id) return Api.Fail("No puede eliminar su propia cuenta", 400);
            var u = await db.Usuarios.FindAsync(id);
            if (u is null) return Api.Fail("Usuario no encontrado", 404);
            db.Usuarios.Remove(u);
            await db.SaveChangesAsync();
            logger.LogInformation("Usuario eliminado {UsuarioId}", id);
            return Api.Ok(new { eliminado = id });
        });
    }
}
