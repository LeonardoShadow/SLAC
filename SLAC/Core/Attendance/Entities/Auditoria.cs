namespace SLAC.Core.Attendance.Entities;

/// <summary>
/// Representa una entrada inmutable en el registro de auditoría institucional (SRS 4.6).
/// Se utiliza para auditar eventos críticos: corrección manual de faltas, suspensión de clases
/// y revinculación/cambio de dispositivos estudiantiles.
/// </summary>
public class Auditoria
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? InstitucionId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Evento { get; set; } = string.Empty;
    public string Entidad { get; set; } = string.Empty;
    public string? DatosJson { get; set; }
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
