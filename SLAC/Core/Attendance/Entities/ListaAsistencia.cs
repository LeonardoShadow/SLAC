namespace SLAC.Core.Attendance.Entities;

/// <summary>
/// Representa la lista maestra de asistencia diaria para una clase específica (SRS 4.5).
/// Estados permitidos: 'Programada', 'Abierta', 'Cerrada', 'Suspendida'.
/// </summary>
public class ListaAsistencia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public Guid MateriaId { get; set; }
    public Guid DocenteId { get; set; }
    public Guid EspacioId { get; set; }
    public DateOnly Fecha { get; set; }
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan? HoraCierre { get; set; }
    public string Estado { get; set; } = "Programada";
    public string? UrlDetalle { get; set; }
    public int TotalSuscritos { get; set; }
    public int TotalPresentes { get; set; }
    public int TotalFaltas { get; set; }
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ActualizadoEn { get; set; } = DateTimeOffset.UtcNow;
}
