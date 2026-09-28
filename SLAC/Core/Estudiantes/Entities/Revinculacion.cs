namespace SLAC.Core.Estudiantes.Entities;

/// <summary>
/// Representa una solicitud de revinculación/cambio de dispositivo móvil de un estudiante (SRS RN-05).
/// Requiere la autorización explícita del docente de la asignatura para proceder con el reemplazo de la credencial previa.
/// </summary>
public class Revinculacion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public Guid EstudianteId { get; set; }
    public Guid MateriaId { get; set; }
    public Guid DocenteId { get; set; }
    public DateTimeOffset ExpiraEn { get; set; } = DateTimeOffset.UtcNow.AddHours(2);
    public DateTimeOffset? UsadaEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;

    // Propiedades de navegación / presentación
    public string? EstudianteNombre { get; set; }
    public string? EstudianteCodigo { get; set; }
    public string? DispositivoAgente { get; set; }
    public bool EstaPendiente => !UsadaEn.HasValue && ExpiraEn > DateTimeOffset.UtcNow;
}
