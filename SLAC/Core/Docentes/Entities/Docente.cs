namespace SLAC.Core.Docentes.Entities;

/// <summary>
/// Representa al perfil del docente en el sistema SLAC.
/// Un docente gestiona y proyecta la asistencia de sus materias asignadas.
/// </summary>
public class Docente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public Guid UserId { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;

    public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();
}
