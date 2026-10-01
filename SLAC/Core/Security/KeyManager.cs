using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SLAC.Core.Security;

/// <summary>
/// Implementación de IKeyManager utilizando System.Security.Cryptography.ECDsa
/// con la curva estándar NIST P-256 (secp256r1) para cumplir con el estándar ES256.
/// </summary>
public class KeyManager : IKeyManager, IDisposable
{
    private const string DefaultMasterPem = """
    -----BEGIN EC PRIVATE KEY-----
    MHcCAQEEIGiTeL+tSZK8p3AO32Quu8TH8zcKOAiFgZKkaVaZJdBIoAoGCCqGSM49
    AwEHoUQDQgAErr+KPTwEk5SCtRp2Oug/uYstceOGjVJC2chdV4E9hS3OLBTne13b
    dkWqOJ+w6dZrWzOnszSMUDGScnphEuSwUw==
    -----END EC PRIVATE KEY-----
    """;

    private readonly ConcurrentDictionary<string, ECDsa> _keyStore = new();
    private readonly string _activeKid;
    private readonly ECDsa _activeKey;

    public KeyManager(Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        _activeKid = "slac-master-k1";
        var pem = configuration?["Security:SigningKeyPem"]
                  ?? Environment.GetEnvironmentVariable("SLAC_SIGNING_KEY_PEM");

        if (string.IsNullOrWhiteSpace(pem))
        {
            pem = DefaultMasterPem;
        }

        try
        {
            _activeKey = ECDsa.Create();
            _activeKey.ImportFromPem(pem);
        }
        catch
        {
            _activeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }

        _keyStore.TryAdd(_activeKid, _activeKey);
    }

    public KeyManager(string kid, ECDsa key)
    {
        _activeKid = kid;
        _activeKey = key;
        _keyStore.TryAdd(_activeKid, _activeKey);
    }

    public ECDsa GetActiveSigningKey(out string kid)
    {
        kid = _activeKid;
        return _activeKey;
    }

    public ECDsa? GetVerificationKey(string kid)
    {
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
