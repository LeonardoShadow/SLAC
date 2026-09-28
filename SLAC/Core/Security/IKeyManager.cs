using System.Security.Cryptography;

namespace SLAC.Core.Security;

/// <summary>
/// Gestiona las claves criptográficas asimétricas ECDSA P-256 (ES256) y sus identificadores 'kid'.
/// Permite rotación de claves y desacopla la firma de la verificación (SRS 4.4 y SECURITY.md).
/// </summary>
public interface IKeyManager
{
    /// <summary>
    /// Retorna la clave ECDsa activa para firmar nuevos tokens y su respectivo 'kid'.
    /// </summary>
    ECDsa GetActiveSigningKey(out string kid);

    /// <summary>
    /// Retorna la clave ECDsa (pública o con parámetros de verificación) correspondiente a un 'kid'.
    /// Retorna null si el 'kid' es desconocido o fue revocado de raíz.
    /// </summary>
    ECDsa? GetVerificationKey(string kid);
}
