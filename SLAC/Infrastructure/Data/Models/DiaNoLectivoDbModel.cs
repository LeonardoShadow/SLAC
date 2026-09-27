using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("dia_no_lectivo")]
public class DiaNoLectivoDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("fecha")]
    public string Fecha { get; set; } = string.Empty;

    [Column("motivo")]
    public string Motivo { get; set; } = string.Empty;

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
