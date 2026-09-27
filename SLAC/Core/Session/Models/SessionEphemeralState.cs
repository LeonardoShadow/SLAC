namespace SLAC.Core.Session.Models;

/// <summary>
/// Estado efímero de una sesión de clase activa almacenado en Redis bajo la clave 'slac:sesion:{id}' (SRS 4.7).
/// Cuenta con TTL igual al vencimiento de la ventana y se elimina al cierre.
/// </summary>
public class SessionEphemeralState
{
    public Guid SesionId { get; set; }
    public Guid InstitucionId { get; set; }
    public Guid MateriaId { get; set; }
    public Guid DocenteId { get; set; }
    public DateTimeOffset InicioVigencia { get; set; }
    public DateTimeOffset Vencimiento { get; set; }
    public int RotacionIndex { get; set; }
    public string TokenActual { get; set; } = string.Empty;
    public int ContadorAsistentes { get; set; }
    public bool EstaAbierta { get; set; } = true;
}
