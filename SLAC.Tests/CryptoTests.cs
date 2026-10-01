using SLAC.Core.Security;
using Xunit;

namespace SLAC.Tests;

public class CryptoTests
{
    private readonly KeyManager _keyManager;
    private readonly TokenService _tokenService;

    public CryptoTests()
    {
        _keyManager = new KeyManager();
        _tokenService = new TokenService(_keyManager);
    }

    [Fact]
    public void DeviceCredential_ShouldSurviveKeyManagerRestart()
    {
        // Instancia 1: Servidor antes del reinicio
        var km1 = new KeyManager();
        var ts1 = new TokenService(km1);
        var dispId = Guid.NewGuid();
        var instId = Guid.NewGuid();
        var (tokenString, _) = ts1.GenerateDeviceCredential(dispId, instId);

        // Instancia 2: Servidor después de borrar wwwroot y reiniciar proceso
        var km2 = new KeyManager();
        var ts2 = new TokenService(km2);
        var isValid = ts2.TryValidateDeviceCredential(tokenString, out var payload, out var error);

        Assert.True(isValid);
        Assert.Null(error);
        Assert.NotNull(payload);
        Assert.Equal(dispId, payload.DispositivoId);
    }

    [Fact]
    public void SessionQrToken_ValidToken_ShouldValidateSuccessfully()
    {
        // Arrange
        var sesionId = Guid.NewGuid();
        var institucionId = Guid.NewGuid();
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5); // Empezó hace 5 min
        const int ventanaMin = 20;
        var rotacionIdx = (int)((DateTimeOffset.UtcNow.ToUnixTimeSeconds() - inicio.ToUnixTimeSeconds()) / 15);

        // Act
        var token = _tokenService.GenerateSessionQrToken(sesionId, institucionId, inicio, ventanaMin, rotacionIdx);
        var isValid = _tokenService.TryValidateSessionQrToken(token, out var payload, out var error);

        // Assert
        Assert.True(isValid);
        Assert.Null(error);
        Assert.NotNull(payload);
        Assert.Equal(sesionId, payload.SesionId);
        Assert.Equal(institucionId, payload.InstitucionId);
    }

    [Fact]
    public void SessionQrToken_TamperedToken_ShouldFailSignatureVerification()
    {
        // Arrange
        var token = _tokenService.GenerateSessionQrToken(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 20, 0);
        var parts = token.Split('.');

        // Modificamos un caracter del payload
        var tamperedPayload = string.Concat(parts[1].AsSpan(0, parts[1].Length - 1), parts[1].EndsWith('A') ? "B" : "A");
        var tamperedToken = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        // Act
        var isValid = _tokenService.TryValidateSessionQrToken(tamperedToken, out var payload, out var error);

        // Assert
        Assert.False(isValid);
        Assert.Null(payload);
        Assert.Contains("adulterado", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionQrToken_ExpiredWindow_ShouldFailValidation()
    {
        // Arrange - Sesión que empezó hace 25 minutos con ventana de 20 minutos
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-25);
        var token = _tokenService.GenerateSessionQrToken(Guid.NewGuid(), Guid.NewGuid(), inicio, 20, 100);

        // Act
        var isValid = _tokenService.TryValidateSessionQrToken(token, out var payload, out var error);

        // Assert
        Assert.False(isValid);
        Assert.Null(payload);
        Assert.Contains("vencido", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeviceCredential_GeneratesAndValidatesSuccessfully()
    {
        // Arrange
        var dispositivoId = Guid.NewGuid();
        var institucionId = Guid.NewGuid();

        // Act
        var (tokenString, jtiHash) = _tokenService.GenerateDeviceCredential(dispositivoId, institucionId);
        var isValid = _tokenService.TryValidateDeviceCredential(tokenString, out var payload, out var error);

        // Assert
        Assert.True(isValid);
        Assert.Null(error);
        Assert.NotNull(payload);
        Assert.Equal(dispositivoId, payload.DispositivoId);
        Assert.Equal(institucionId, payload.InstitucionId);
        Assert.Equal(64, jtiHash.Length); // Hash SHA256 hex de 64 caracteres
    }
}
