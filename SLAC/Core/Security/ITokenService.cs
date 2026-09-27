using SLAC.Core.Security.Models;

namespace SLAC.Core.Security;

/// <summary>
/// Contrato para generación, serialización y validación estricta de:
/// 1. Token de Sesión (código QR rotativo de aula).
/// 2. Credencial de Dispositivo (cookie HttpOnly del estudiante).
/// Cumple con SRS 4.4 y RFC 7515 / ECDSA SHA-256.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Genera el token firmado que se embebe en el código QR dinámico proyectado.
    /// </summary>
    string GenerateSessionQrToken(
        Guid sesionId, 
        Guid institucionId, 
        DateTimeOffset inicioVigencia, 
        int ventanaMinutos, 
        int rotacionIndex);

    /// <summary>
    /// Valida la firma ES256, vigencia de la ventana y tolerancia de rotación de un token QR.
    /// </summary>
    bool TryValidateSessionQrToken(
        string tokenString, 
        out SessionQrToken? sessionToken, 
        out string? errorMessage,
        int rotacionTolerancia = 1);

    /// <summary>
    /// Emite la credencial criptográfica de dispositivo para el navegador del estudiante.
    /// Retorna la cadena del token para la cookie y el hash SHA256 del jti para persistir en BD.
    /// </summary>
    (string TokenString, string JtiHash) GenerateDeviceCredential(Guid dispositivoId, Guid institucionId);

    /// <summary>
    /// Valida la firma ES256 y la integridad de la credencial del dispositivo recibida en la cookie.
    /// </summary>
    bool TryValidateDeviceCredential(
        string tokenString, 
        out DeviceCredentialToken? deviceToken, 
        out string? errorMessage);
}
