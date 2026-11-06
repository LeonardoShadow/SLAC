using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using SLAC.Core.Security;
using Xunit;

namespace SLAC.Tests;

public class CryptoTests
{
    private static readonly object s_envLock = new();

    private const string SecondaryValidPem = """
    -----BEGIN EC PRIVATE KEY-----
    MHcCAQEEIEsBvkq8e/WDS1K6yhd67CsCVomkhpKTKWWLUtR5boXaoAoGCCqGSM49
    AwEHoUQDQgAEuZV0DIjwEoXlDUsCf37Nw4IOCgy4vcS23SO/QuyLKsCF5gDc33kp
    Tip+YzcSN/9Pvm5LDr44IiW1Jv7sMg4H4w==
    -----END EC PRIVATE KEY-----
    """;

    private const string SecondaryValidPublicKeyPem = """
    -----BEGIN PUBLIC KEY-----
    MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEuZV0DIjwEoXlDUsCf37Nw4IOCgy4
    vcS23SO/QuyLKsCF5gDc33kpTip+YzcSN/9Pvm5LDr44IiW1Jv7sMg4H4w==
    -----END PUBLIC KEY-----
    """;

    private const string TestPublicKeyPem = """
    -----BEGIN PUBLIC KEY-----
    MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEGXKnE8T1NHE68eOrjkccTSNn6EZN
    dMzhz1XGdF7FseuWlZMd6OZdOFdESPNEb+KgNAVcYloI9pSMdFzL32Ppww==
    -----END PUBLIC KEY-----
    """;

    private readonly KeyManager _keyManager;
    private readonly TokenService _tokenService;

    public CryptoTests()
    {
        _keyManager = new KeyManager();
        _tokenService = new TokenService(_keyManager);
    }

    private static IConfiguration CreateConfig(string? pemValue)
    {
        var dict = new Dictionary<string, string?>();
        if (pemValue != null)
        {
            dict["Security:SigningKeyPem"] = pemValue;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    private static IConfiguration CreateRealFileConfig()
    {
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            appSettingsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../SLAC/appsettings.json"));
        }

        return new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: false)
            .Build();
    }

    private static void WithEnvironmentVariable(string? value, Action action)
    {
        lock (s_envLock)
        {
            var original = Environment.GetEnvironmentVariable("SLAC_SIGNING_KEY_PEM");
            try
            {
                Environment.SetEnvironmentVariable("SLAC_SIGNING_KEY_PEM", value);
                action();
            }
            finally
            {
                Environment.SetEnvironmentVariable("SLAC_SIGNING_KEY_PEM", original);
            }
        }
    }

    [Fact]
    public void DeviceCredential_ShouldSurviveKeyManagerRestart()
    {
        // Instancia 1: Servidor antes del reinicio
        using var km1 = new KeyManager();
        var ts1 = new TokenService(km1);
        var dispId = Guid.NewGuid();
        var instId = Guid.NewGuid();
        var (tokenString, _) = ts1.GenerateDeviceCredential(dispId, instId);

        // Instancia 2: Servidor después de borrar wwwroot y reiniciar proceso
        using var km2 = new KeyManager();
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
    public void SessionQrToken_StaticNonRotatingToken_ShouldPassValidationThroughoutClassWindow()
    {
        // Arrange - Sesión que empezó hace 8 minutos con ventana de 20 minutos (rotacionIndex = -1 para QR Único)
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-8);
        var sesionId = Guid.NewGuid();
        var institucionId = Guid.NewGuid();
        var tokenEstatico = _tokenService.GenerateSessionQrToken(sesionId, institucionId, inicio, 20, -1);

        // Act - Se valida con validarRotacion = true
        var isValid = _tokenService.TryValidateSessionQrToken(tokenEstatico, out var payload, out var error, validarRotacion: true);

        // Assert - A pesar de que pasaron 8 minutos (32 rotaciones de 15s), debe ser 100% válido por ser QR Único
        Assert.True(isValid);
        Assert.Null(error);
        Assert.NotNull(payload);
        Assert.Equal(-1, payload.RotacionIndex);
        Assert.Equal(sesionId, payload.SesionId);
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

    [Fact]
    public void KeyManager_WithoutConfigurationOrEnvironmentVariable_ThrowsInvalidOperationException()
    {
        WithEnvironmentVariable(null, () =>
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(null));
            Assert.Contains("[SLAC] CRÍTICO", ex.Message);
        });
    }

    [Fact]
    public void KeyManager_WithCorruptPem_ThrowsInvalidOperationException()
    {
        WithEnvironmentVariable("CORRUPT_KEY_PEM_CONTENT", () =>
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(null));
            Assert.Contains("[SLAC] CRÍTICO: La clave PEM proporcionada no es válida o está corrupta", ex.Message);
        });
    }

    // =========================================================================
    // CONFIGURATION RESOLUTION & FALLBACK TESTS (CRS)
    // =========================================================================

    [Fact]
    public void KeyManager_WithEmptyConfigString_FallsBackToEnvironmentVariable_Successfully()
    {
        // Regresión VULN-01: appsettings.json define "SigningKeyPem": "" y el administrador
        // define la clave en SLAC_SIGNING_KEY_PEM. KeyManager no debe evaluar "" como valor válido.
        var config = CreateConfig("");
        using var km = new KeyManager(config);
        var key = km.GetActiveSigningKey(out var kid);

        Assert.NotNull(key);
        Assert.Equal("slac-master-k1", kid);

        // Prueba funcional de emisión y verificación con TokenService
        var ts = new TokenService(km);
        var (token, _) = ts.GenerateDeviceCredential(Guid.NewGuid(), Guid.NewGuid());
        Assert.True(ts.TryValidateDeviceCredential(token, out _, out _));
    }

    [Fact]
    public void KeyManager_WithWhitespaceConfigString_FallsBackToEnvironmentVariable_Successfully()
    {
        var config = CreateConfig("   \t\r\n   ");
        using var km = new KeyManager(config);
        var key = km.GetActiveSigningKey(out var kid);

        Assert.NotNull(key);
        Assert.Equal("slac-master-k1", kid);
    }

    [Fact]
    public void KeyManager_WithMissingKeyInConfig_FallsBackToEnvironmentVariable_Successfully()
    {
        var config = CreateConfig(null);
        using var km = new KeyManager(config);
        var key = km.GetActiveSigningKey(out var kid);

        Assert.NotNull(key);
        Assert.Equal("slac-master-k1", kid);
    }

    [Fact]
    public void KeyManager_WithRealAppSettingsFile_FallsBackToEnvironmentVariable_Successfully()
    {
        var config = CreateRealFileConfig();
        using var km = new KeyManager(config);
        var key = km.GetActiveSigningKey(out var kid);

        Assert.NotNull(key);
        Assert.Equal("slac-master-k1", kid);
    }

    [Fact]
    public void KeyManager_WhenBothConfigAndEnvironmentVariableProvided_ConfigTakesPrecedence()
    {
        // Arrange: Proveer SecondaryValidPem en config y TestSigningKeyPem en env var
        var config = CreateConfig(SecondaryValidPem);

        WithEnvironmentVariable(TestAssemblySetup.TestSigningKeyPem, () =>
        {
            // Act
            using var km = new KeyManager(config);
            var activeKey = km.GetActiveSigningKey(out var kid);

            // Assert
            Assert.NotNull(activeKey);
            Assert.Equal("slac-master-k1", kid);

            byte[] dataToSign = [1, 2, 3, 4, 5, 6, 7, 8];
            var signature = activeKey.SignData(dataToSign, HashAlgorithmName.SHA256);

            // SecondaryValidPublicKeyPem DEBE validar la firma (Config ganó precedencia)
            using var secondaryPub = ECDsa.Create();
            secondaryPub.ImportFromPem(SecondaryValidPublicKeyPem);
            Assert.True(secondaryPub.VerifyData(dataToSign, signature, HashAlgorithmName.SHA256));

            // TestPublicKeyPem (Env var) NO DEBE validar la firma
            using var envPub = ECDsa.Create();
            envPub.ImportFromPem(TestPublicKeyPem);
            Assert.False(envPub.VerifyData(dataToSign, signature, HashAlgorithmName.SHA256));
        });
    }

    [Fact]
    public void KeyManager_WithValidConfigAndNoEnvironmentVariable_BootsSuccessfully()
    {
        var config = CreateConfig(TestAssemblySetup.TestSigningKeyPem);

        WithEnvironmentVariable(null, () =>
        {
            using var km = new KeyManager(config);
            var key = km.GetActiveSigningKey(out var kid);

            Assert.NotNull(key);
            Assert.Equal("slac-master-k1", kid);
        });
    }

    [Fact]
    public void KeyManager_WhenConfigIsEmptyStringAndEnvVarMissing_ThrowsInvalidOperationException()
    {
        WithEnvironmentVariable(null, () =>
        {
            var config = CreateConfig("");
            var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
            Assert.Contains("[SLAC] CRÍTICO", ex.Message);
            Assert.Contains("SLAC_SIGNING_KEY_PEM", ex.Message);
        });
    }

    [Fact]
    public void KeyManager_WhenConfigIsWhitespaceAndEnvVarMissing_ThrowsInvalidOperationException()
    {
        WithEnvironmentVariable(null, () =>
        {
            var config = CreateConfig("   \t   ");
            var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
            Assert.Contains("[SLAC] CRÍTICO", ex.Message);
        });
    }

    [Fact]
    public void KeyManager_WhenConfigHasEmptyStringAndEnvVarIsWhitespace_ThrowsInvalidOperationException()
    {
        WithEnvironmentVariable("   \r\n   ", () =>
        {
            var config = CreateConfig("");
            var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
            Assert.Contains("[SLAC] CRÍTICO", ex.Message);
        });
    }

    [Fact]
    public void KeyManager_WhenConfigMissingKeyAndEnvVarMissing_ThrowsInvalidOperationException()
    {
        WithEnvironmentVariable(null, () =>
        {
            var config = CreateConfig(null);
            var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
            Assert.Contains("[SLAC] CRÍTICO", ex.Message);
        });
    }

    [Fact]
    public void KeyManager_WhenConfigHasCorruptedPem_ThrowsInvalidOperationException_WithoutFallingBackToEnvVar()
    {
        var config = CreateConfig("GARBAGE_PEM_CONTENT_CORRUPTED");

        var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
        Assert.Contains("[SLAC] CRÍTICO: La clave PEM proporcionada no es válida o está corrupta", ex.Message);
    }

    [Fact]
    public void KeyManager_WhenConfigIsEmptyAndEnvVarHasCorruptedPem_ThrowsInvalidOperationException()
    {
        WithEnvironmentVariable("CORRUPTED_ENV_VAR_PEM", () =>
        {
            var config = CreateConfig("");
            var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
            Assert.Contains("[SLAC] CRÍTICO: La clave PEM proporcionada no es válida o está corrupta", ex.Message);
        });
    }

    [Fact]
    public void KeyManager_WithTruncatedPem_ThrowsInvalidOperationException()
    {
        var truncated = TestAssemblySetup.TestSigningKeyPem[..70];
        var config = CreateConfig(truncated);

        var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
        Assert.Contains("[SLAC] CRÍTICO: La clave PEM proporcionada no es válida o está corrupta", ex.Message);
    }

    [Fact]
    public void KeyManager_WithRsaPrivateKeyPem_ThrowsInvalidOperationException()
    {
        using var rsa = RSA.Create(2048);
        var rsaPem = rsa.ExportRSAPrivateKeyPem();
        var config = CreateConfig(rsaPem);

        var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
        Assert.Contains("[SLAC] CRÍTICO: La clave PEM proporcionada no es válida o está corrupta", ex.Message);
    }

    [Fact]
    public void KeyManager_WithPublicKeyOnlyPem_ThrowsInvalidOperationException()
    {
        var config = CreateConfig(SecondaryValidPublicKeyPem);
        var ex = Assert.Throws<InvalidOperationException>(() => new KeyManager(config));
        Assert.Contains("[SLAC] CRÍTICO: La clave PEM proporcionada no contiene una clave privada válida para firma criptográfica", ex.Message);
    }

    [Fact]
    public void KeyManager_GetVerificationKey_WithNullOrEmptyKid_ReturnsNullWithoutThrowing()
    {
        using var km = new KeyManager();
        Assert.Null(km.GetVerificationKey(null!));
        Assert.Null(km.GetVerificationKey(""));
        Assert.Null(km.GetVerificationKey("   "));
    }

    [Fact]
    public void KeyManager_GetVerificationKey_WithUnknownKid_ReturnsNull()
    {
        using var km = new KeyManager();
        Assert.Null(km.GetVerificationKey("kid-inexistente-12345"));
    }

    [Fact]
    public void KeyManager_GetVerificationKey_WithLegacySlacPrefix_ReturnsActiveKey()
    {
        using var km = new KeyManager();
        var legacyKey = km.GetVerificationKey("slac-legacy-token-v1");
        Assert.NotNull(legacyKey);
    }

    [Fact]
    public void KeyManager_ManualConstructor_DefensiveValidation()
    {
        using var dummyKey = ECDsa.Create();
        Assert.Throws<ArgumentException>(() => new KeyManager("", dummyKey));
        Assert.Throws<ArgumentException>(() => new KeyManager("   ", dummyKey));
        Assert.Throws<ArgumentNullException>(() => new KeyManager("valid-kid", null!));
    }

    [Fact]
    public void KeyManager_RegisterLegacyKey_DefensiveValidation()
    {
        using var km = new KeyManager();
        using var dummyKey = ECDsa.Create();
        Assert.Throws<ArgumentException>(() => km.RegisterLegacyKey("", dummyKey));
        Assert.Throws<ArgumentException>(() => km.RegisterLegacyKey("   ", dummyKey));
        Assert.Throws<ArgumentNullException>(() => km.RegisterLegacyKey("valid-kid", null!));

        km.RegisterLegacyKey("custom-legacy-1", dummyKey);
        var retrieved = km.GetVerificationKey("custom-legacy-1");
        Assert.Same(dummyKey, retrieved);
    }
}
