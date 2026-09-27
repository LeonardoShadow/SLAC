namespace SLAC.Core.Attendance.Entities;

/// <summary>
/// Representa el vínculo entre un estudiante y una materia tras el primer escaneo de QR (SRS 4.5).
/// </summary>
public class Suscripcion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public Guid MateriaId { get; set; }
    public Guid EstudianteId { get; set; }
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ListaOrigenId { get; set; }
    public string Estado { get; set; } = "activa";
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
