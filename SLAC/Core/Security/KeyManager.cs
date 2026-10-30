using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SLAC.Core.Security;

/// <summary>
/// Implementación de IKeyManager utilizando System.Security.Cryptography.ECDsa
/// con la curva estándar NIST P-256 (secp256r1) para cumplir con el estándar ES256.
/// </summary>
public class KeyManager : IKeyManager, IDisposable
{
    private readonly ConcurrentDictionary<string, ECDsa> _keyStore = new();
    private readonly string _activeKid;
    private readonly ECDsa _activeKey;

    public KeyManager(Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        _activeKid = "slac-master-k1";

        // Coalescencia defensiva: Si la configuración provee una cadena vacía o sólo espacios (ej. appsettings.json),
        // se recurre a la variable de entorno 'SLAC_SIGNING_KEY_PEM'.
        var pem = configuration?["Security:SigningKeyPem"];
        if (string.IsNullOrWhiteSpace(pem))
        {
            pem = Environment.GetEnvironmentVariable("SLAC_SIGNING_KEY_PEM");
        }

        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                "[SLAC] CRÍTICO: No se encontró la clave de firma ECDSA. Configure la variable de entorno 'SLAC_SIGNING_KEY_PEM' o 'Security:SigningKeyPem' en la configuración del servidor. La aplicación no puede iniciarse sin una clave criptográfica segura.");
        }

        try
        {
            _activeKey = ECDsa.Create();
            _activeKey.ImportFromPem(pem);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[SLAC] CRÍTICO: La clave PEM proporcionada no es válida o está corrupta: {ex.Message}", ex);
        }

        // Validación Fail-Fast: Asegura que la clave importada contiene componentes privados válidos para firma (no es sólo una clave pública)
        try
        {
            Span<byte> dummyData = stackalloc byte[1];
            _ = _activeKey.SignData(dummyData, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"[SLAC] CRÍTICO: La clave PEM proporcionada no contiene una clave privada válida para firma criptográfica (posible clave pública): {ex.Message}", ex);
        }

        _keyStore.TryAdd(_activeKid, _activeKey);
    }

    public KeyManager(string kid, ECDsa key)
    {
        if (string.IsNullOrWhiteSpace(kid))
        {
            throw new ArgumentException("El identificador de clave (kid) no puede ser nulo ni vacío.", nameof(kid));
        }
        _activeKid = kid;
        _activeKey = key ?? throw new ArgumentNullException(nameof(key));
        _keyStore.TryAdd(_activeKid, _activeKey);
    }

    public ECDsa GetActiveSigningKey(out string kid)
    {
        kid = _activeKid;
        return _activeKey;
    }

    public ECDsa? GetVerificationKey(string? kid)
    {
        if (string.IsNullOrWhiteSpace(kid))
        {
            return null;
        }

        if (_keyStore.TryGetValue(kid, out var key))
        {
            return key;
        }

        // Retrocompatibilidad con tokens emitidos con formato dinámico previo (slac-*)
        if (kid.StartsWith("slac-", StringComparison.OrdinalIgnoreCase))
        {
            return _activeKey;
        }

        return null;
    }

    public void RegisterLegacyKey(string kid, ECDsa key)
    {
        if (string.IsNullOrWhiteSpace(kid))
        {
            throw new ArgumentException("El identificador de clave (kid) no puede ser nulo ni vacío.", nameof(kid));
        }
        ArgumentNullException.ThrowIfNull(key);
        _keyStore.TryAdd(kid, key);
    }

    public void Dispose()
    {
        foreach (var key in _keyStore.Values)
        {
            key.Dispose();
        }
        _keyStore.Clear();
        GC.SuppressFinalize(this);
    }
}
