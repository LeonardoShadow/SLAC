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

    public KeyManager()
    {
        // En producción puede cargarse desde Azure KeyVault, AWS KMS o un secreto cifrado.
        // Inicializamos con una llave ECDSA P-256 de alta entropía.
        _activeKid = $"slac-{DateTime.UtcNow:yyyyMM}-k1";
        _activeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
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
