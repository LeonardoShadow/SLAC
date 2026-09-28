namespace SLAC.Core.Estudiantes.Entities;

/// <summary>
/// Entidad de dominio que representa a un estudiante universitario/escolar.
/// Los estudiantes son actores anónimos en el sistema de autenticación de Supabase (no tienen auth.users),
/// y su identidad institucional se valida mediante código único y credenciales ES256 en su dispositivo (AGENTS.md, DATABASE.md).
/// </summary>
public class Estudiante
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public DateTimeOffset? ConsentimientoEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;

    public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();
}
