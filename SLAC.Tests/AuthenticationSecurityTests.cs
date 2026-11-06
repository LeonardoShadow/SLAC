using Microsoft.Extensions.Logging.Abstractions;
using SLAC.Core.Docentes.Entities;
using SLAC.Core.Institucional.Entities;
using SLAC.Infrastructure.Repositories;
using Xunit;

namespace SLAC.Tests;

public class AuthenticationSecurityTests
{
    private readonly InMemoryAdministradorInstitucionalRepository _adminRepo = new();

    [Fact]
    public void SuperAdmin_BcryptHash_ShouldVerifyCorrectPassword()
    {
        var password = "SuperAdminSecret2026!";
        var hash = BCrypt.Net.BCrypt.HashPassword(password, 11);

        var verified = BCrypt.Net.BCrypt.Verify(password, hash);
        Assert.True(verified, "La contraseña autorizada debe verificar correctamente con BCrypt.");

        // El viejo PIN de backdoor "78161193" NO debe verificar
        var verifiedWrong = BCrypt.Net.BCrypt.Verify("78161193", hash);
        Assert.False(verifiedWrong, "El antiguo PIN backdoor '78161193' no debe verificar.");
    }

    [Fact]
    public void SuperAdmin_GeneratesValidHashForDefaultCode()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("75604458", 10);
        Assert.True(BCrypt.Net.BCrypt.Verify("75604458", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("78161193", hash));
    }

    [Fact]
    public async Task AdminInstitucional_ValidBcryptPassword_ShouldAuthenticate()
    {
        var admin = await _adminRepo.ValidarCredencialesAsync("sc.upds.m@upds.net.bo", "75604458");
        Assert.NotNull(admin);
        Assert.Equal("Administrador UPDS", admin.NombreCompleto);
    }

    [Fact]
    public async Task AdminInstitucional_WrongPassword_ShouldReturnNull()
    {
        var admin = await _adminRepo.ValidarCredencialesAsync("sc.upds.m@upds.net.bo", "clave_totalmente_invalida");
        Assert.Null(admin);
    }

    [Fact]
    public void Docente_PasswordHash_ShouldVerifySuccessfully()
    {
        var passwordPlano = "DocenteSeguro2026!";
        var hash = BCrypt.Net.BCrypt.HashPassword(passwordPlano, 11);

        var docente = new Docente
        {
            Id = Guid.NewGuid(),
            Nombres = "Carlos",
            Apellidos = "Mendoza",
            Correo = "carlos.mendoza@upds.net.bo",
            Codigo = "DOC-101",
            PasswordHash = hash
        };

        var verificado = BCrypt.Net.BCrypt.Verify(passwordPlano, docente.PasswordHash);
        var fallo = BCrypt.Net.BCrypt.Verify("otraClave", docente.PasswordHash);

        Assert.True(verificado, "La contraseña del docente debe ser verificada exitosamente por BCrypt.");
        Assert.False(fallo, "Contraseña incorrecta debe ser rechazada.");
    }
}
