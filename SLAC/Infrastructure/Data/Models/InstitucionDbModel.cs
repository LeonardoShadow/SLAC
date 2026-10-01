using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("institucion")]
public class InstitucionDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Column("tipo")]
    public string Tipo { get; set; } = "universidad";

    [Column("zona_horaria")]
    public string ZonaHoraria { get; set; } = "America/La_Paz";

    [Column("ventana_min")]
    public int VentanaMin { get; set; } = 20;

    [Column("rotacion_seg")]
    public int RotacionSeg { get; set; } = 15;

    [Column("estado")]
    public string Estado { get; set; } = "activo";

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    [Column("actualizado_en")]
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
}
