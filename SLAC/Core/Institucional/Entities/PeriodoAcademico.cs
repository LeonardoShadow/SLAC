namespace SLAC.Core.Institucional.Entities;

/// <summary>
/// Periodo académico (semestres, bimestres) con fecha de inicio y fin (SRS 4.5 y RF-04).
/// </summary>
public class PeriodoAcademico
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public DateOnly FechaInicio { get; set; }
    public DateOnly FechaFin { get; set; }
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
