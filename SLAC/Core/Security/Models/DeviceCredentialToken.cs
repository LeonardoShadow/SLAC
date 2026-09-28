namespace SLAC.Core.Security.Models;

/// <summary>
/// Modelo de la Credencial de Dispositivo emitida al estudiante en su primer escaneo (SRS 4.4).
/// Se almacena en el navegador como cookie HttpOnly, Secure, SameSite=Lax.
/// NO CONTIENE ningún dato personal (nombre, código o correo) para preservar la privacidad.
/// </summary>
public class DeviceCredentialToken
{
    /// <summary>
    /// Identificador único del dispositivo en base de datos.
    /// </summary>
    public Guid DispositivoId { get; set; }

    /// <summary>
    /// ID de la institución educativa a la que pertenece la vinculación.
    /// </summary>
    public Guid InstitucionId { get; set; }

    /// <summary>
    /// JWT ID / Identificador único de este token (su SHA256 se persiste en BD como jti_hash).
    /// </summary>
    public string Jti { get; set; } = string.Empty;

    /// <summary>
    /// Fecha de emisión en Unix Timestamp UTC.
    /// </summary>
    public long EmitidoEnUnix { get; set; }

    /// <summary>
    /// Identificador de la clave usada para la firma ECDSA.
    /// </summary>
    public string Kid { get; set; } = string.Empty;
}
