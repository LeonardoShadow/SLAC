namespace SLAC.Core.Session.Models;

using System;

/// <summary>
/// Estado efímero de una sesión de clase activa almacenado en Redis bajo la clave 'slac:sesion:{id}' (SRS 4.7).
/// Cuenta con TTL igual al vencimiento de la ventana y se elimina al cierre.
/// </summary>
public class SessionEphemeralState
{
    public Guid SesionId { get; set; }
    public Guid InstitucionId { get; set; }
    public Guid MateriaId { get; set; }
    public Guid DocenteId { get; set; }
    public DateTimeOffset InicioVigencia { get; set; }
    public DateTimeOffset Vencimiento { get; set; }
    public int RotacionIndex { get; set; }
    public string TokenActual { get; set; } = string.Empty;
    public int ContadorAsistentes { get; set; }
    public bool EstaAbierta { get; set; } = true;

    // Configuración Multi-Campus y Reto Visual Dinámico
    public string CampusCode { get; set; } = "UPDS_SUR";
    public string CodigoPresenciaActual { get; set; } = string.Empty;

    // Autoridad Geográfica GPS Alpha (Calibración Docente)
    public double? DocenteLatitudAlpha { get; set; }
    public double? DocenteLongitudAlpha { get; set; }
    public double? DocentePrecisionAlpha { get; set; }
    public double? VarianzaKalmanDocente { get; set; }
    public string ModoValidacionActivo { get; set; } = "gps_alpha";
    public int RadioNominalAulaMetros { get; set; } = 12;
    public DateTimeOffset? CalibradoEnUtc { get; set; }
    public DateTimeOffset? DocenteCalibracionTimestamp
    {
        get => CalibradoEnUtc;
        set => CalibradoEnUtc = value;
    }
    public string FuenteCalibracion { get; set; } = "smartphone_sync";

    // Concurrencia y Bloqueo de Ventana Activa Única de Proyección
    public string? ProjectionLeaseId { get; set; }
    public DateTimeOffset? ProjectionLockedAtUtc { get; set; }
}
