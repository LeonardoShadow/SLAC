namespace SLAC.Core.Institucional.Entities;

/// <summary>
/// Catálogo de aulas y laboratorios (SRS 4.5 y RF-03).
/// </summary>
public class Espacio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = "aula"; // 'aula', 'laboratorio'
    public int Capacidad { get; set; } = 30;
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
