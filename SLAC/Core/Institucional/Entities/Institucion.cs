namespace SLAC.Core.Institucional.Entities;

/// <summary>
/// Entidad Tenant raíz (SRS 4.5 y DATABASE.md).
/// </summary>
public class Institucion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = "universidad"; // 'universidad', 'colegio', 'instituto'
    public string ZonaHoraria { get; set; } = "America/La_Paz";
    public int VentanaMin { get; set; } = 20;
    public int RotacionSeg { get; set; } = 15;
    public string Estado { get; set; } = "activo"; // 'activo', 'inactivo', 'suspendido'
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ActualizadoEn { get; set; } = DateTimeOffset.UtcNow;
}
