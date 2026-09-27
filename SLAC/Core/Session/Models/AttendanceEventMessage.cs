namespace SLAC.Core.Session.Models;

/// <summary>
/// Mensaje emitido a través del canal Redis Pub/Sub 'slac:sesion:{id}:eventos' (SRS 4.7).
/// </summary>
public class AttendanceEventMessage
{
    public string TipoEvento { get; set; } = string.Empty; // "asistencia_registrada", "qr_rotado", "sesion_cerrada"
    public Guid SesionId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    // Datos para evento: asistencia_registrada
    public int? TotalPresentes { get; set; }
    public int? MinutosDesdeInicio { get; set; }
    public string? EstudianteCodigo { get; set; }

    // Datos para evento: qr_rotado
    public string? NuevoToken { get; set; }
    public int? RotacionIndex { get; set; }

    // Datos para evento: sesion_cerrada
    public int? TotalFaltas { get; set; }
}
