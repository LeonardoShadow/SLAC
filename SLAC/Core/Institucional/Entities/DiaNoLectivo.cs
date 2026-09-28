namespace SLAC.Core.Institucional.Entities;

/// <summary>
/// Día no lectivo / feriado o suspensión institucional (SRS 4.5 y RF-04).
/// Impide la generación automática de sesiones de asistencia en esta fecha.
/// </summary>
public class DiaNoLectivo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public DateOnly Fecha { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
