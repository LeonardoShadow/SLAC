using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("espacio")]
public class EspacioDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Column("tipo")]
    public string Tipo { get; set; } = "aula";

    [Column("capacidad")]
    public int Capacidad { get; set; } = 30;

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
