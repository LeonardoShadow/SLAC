namespace SLAC.Core.Estudiantes.Entities;

/// <summary>
/// Vinculación institucional 1:1 activa entre un dispositivo físico y un estudiante (PRD RF-02).
/// Garantiza la regla de unicidad por la que un estudiante solo puede tener un dispositivo activo a la vez.
/// </summary>
public class DispositivoEstudiante
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DispositivoId { get; set; }
    public Guid EstudianteId { get; set; }
    public Guid InstitucionId { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
