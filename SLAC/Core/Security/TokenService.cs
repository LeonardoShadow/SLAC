using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using SLAC.Core.Security.Models;

namespace SLAC.Core.Security;

public class TokenService(IKeyManager keyManager) : ITokenService
{
    private readonly IKeyManager _keyManager = keyManager;

    private class TokenHeader
    {
        public string Alg { get; set; } = "ES256";
        public string Typ { get; set; } = "JWT";
        public string Kid { get; set; } = string.Empty;
    }

    public string GenerateSessionQrToken(
        Guid sesionId,
        Guid institucionId,
        DateTimeOffset inicioVigencia,
        int ventanaMinutos,
        int rotacionIndex)
    {
        var key = _keyManager.GetActiveSigningKey(out var kid);
        var ahoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var inicioUnix = inicioVigencia.ToUnixTimeSeconds();
        var vencimientoUnix = inicioVigencia.AddMinutes(ventanaMinutos).ToUnixTimeSeconds();

        var tokenPayload = new SessionQrToken
        {
            SesionId = sesionId,
            InstitucionId = institucionId,
            InicioVigenciaUnix = inicioUnix,
            VencimientoUnix = vencimientoUnix,
            RotacionIndex = rotacionIndex,
            Nonce = Guid.NewGuid().ToString("N"),
            Kid = kid
        };

        var header = new TokenHeader
        {
            Alg = "ES256",
            Typ = "SLAC-QR",
            Kid = kid
        };

        return CreateSignedToken(header, tokenPayload, key);
    }

    public bool TryValidateSessionQrToken(
        string tokenString,
        out SessionQrToken? sessionToken,
        out string? errorMessage,
        int rotacionTolerancia = 2)
    {
        sessionToken = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(tokenString))
        {
            errorMessage = "El token proporcionado está vacío.";
            return false;
        }

        var parts = tokenString.Split('.');
        if (parts.Length != 3)
        {
            errorMessage = "Estructura de token inválida.";
            return false;
        }

        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[0]));
            var header = JsonSerializer.Deserialize<TokenHeader>(headerJson);
            if (header == null || header.Alg != "ES256" || string.IsNullOrWhiteSpace(header.Kid))
            {
                errorMessage = "Encabezado o algoritmo de token no compatible.";
                return false;
            }

            var verificationKey = _keyManager.GetVerificationKey(header.Kid);
            if (verificationKey == null)
            {
                errorMessage = "Identificador de clave (kid) desconocido o revocado.";
                return false;
            }

            var dataToSign = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64UrlEncoder.DecodeBytes(parts[2]);

            if (!verificationKey.VerifyData(dataToSign, signature, HashAlgorithmName.SHA256))
            {
                errorMessage = "Firma criptográfica inválida (token adulterado).";
                return false;
            }

            var payloadJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
            var payload = JsonSerializer.Deserialize<SessionQrToken>(payloadJson);
            if (payload == null)
            {
                errorMessage = "Carga útil del token no decodificable.";
                return false;
            }

            var ahoraUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // 1. Validar ventana global de la clase (20 minutos) con margen de reloj
            if (ahoraUnix < payload.InicioVigenciaUnix - 15)
            {
                errorMessage = "La ventana de asistencia aún no ha iniciado.";
                return false;
            }

            if (ahoraUnix > payload.VencimientoUnix + 30)
            {
                errorMessage = "La ventana de asistencia para esta clase ha vencido (token anulado).";
                return false;
            }

            // 2. Validar rotación dinámica (15 segundos) con tolerancia
            var segundosTranscurridos = Math.Max(0, ahoraUnix - payload.InicioVigenciaUnix);
            var indiceEsperado = (int)(segundosTranscurridos / 15);

            if (payload.RotacionIndex < (indiceEsperado - rotacionTolerancia) || payload.RotacionIndex > (indiceEsperado + 1))
            {
                errorMessage = "El código QR ha expirado por rotación. Por favor escanee el código actual.";
                return false;
            }

            sessionToken = payload;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Error al procesar el token: {ex.Message}";
            return false;
        }
    }

    public (string TokenString, string JtiHash) GenerateDeviceCredential(Guid dispositivoId, Guid institucionId)
    {
        var key = _keyManager.GetActiveSigningKey(out var kid);
        var jti = Guid.NewGuid().ToString("N");
        var emitidoEn = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var payload = new DeviceCredentialToken
        {
            DispositivoId = dispositivoId,
            InstitucionId = institucionId,
            Jti = jti,
            EmitidoEnUnix = emitidoEn,
            Kid = kid
        };

        var header = new TokenHeader
        {
            Alg = "ES256",
            Typ = "SLAC-DEV",
            Kid = kid
        };

        var tokenString = CreateSignedToken(header, payload, key);
        var jtiHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jti))).ToLowerInvariant();

        return (tokenString, jtiHash);
    }

    public bool TryValidateDeviceCredential(
        string tokenString,
        out DeviceCredentialToken? deviceToken,
        out string? errorMessage)
    {
        deviceToken = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(tokenString))
        {
            errorMessage = "Credencial de dispositivo ausente.";
            return false;
        }

        var parts = tokenString.Split('.');
        if (parts.Length != 3)
        {
            errorMessage = "Formato de credencial inválido.";
            return false;
        }

        try
        {
            var headerJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[0]));
            var header = JsonSerializer.Deserialize<TokenHeader>(headerJson);
            if (header == null || header.Alg != "ES256" || string.IsNullOrWhiteSpace(header.Kid))
            {
                errorMessage = "Encabezado de credencial inválido.";
                return false;
            }

            var verificationKey = _keyManager.GetVerificationKey(header.Kid);
            if (verificationKey == null)
            {
                errorMessage = "Clave emisora de la credencial no reconocida.";
                return false;
            }

            var dataToSign = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64UrlEncoder.DecodeBytes(parts[2]);

            if (!verificationKey.VerifyData(dataToSign, signature, HashAlgorithmName.SHA256))
            {
                errorMessage = "Firma de credencial adulterada o corrupta.";
                return false;
            }

            var payloadJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
            var payload = JsonSerializer.Deserialize<DeviceCredentialToken>(payloadJson);
            if (payload == null)
            {
                errorMessage = "Contenido de credencial ilegible.";
                return false;
            }

            deviceToken = payload;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Error al verificar credencial: {ex.Message}";
            return false;
        }
    }

    private static string CreateSignedToken<T>(TokenHeader header, T payload, ECDsa key)
    {
        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var encodedHeader = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(headerJson));
        var encodedPayload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));

        var dataToSign = Encoding.UTF8.GetBytes($"{encodedHeader}.{encodedPayload}");
        var signature = key.SignData(dataToSign, HashAlgorithmName.SHA256);
        var encodedSignature = Base64UrlEncoder.Encode(signature);

        return $"{encodedHeader}.{encodedPayload}.{encodedSignature}";
    }
}
