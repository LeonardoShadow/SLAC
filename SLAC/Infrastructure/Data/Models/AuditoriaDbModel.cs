using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("auditoria")]
public class AuditoriaDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid? InstitucionId { get; set; }

    [Column("actor")]
    public string Actor { get; set; } = string.Empty;

    [Column("evento")]
    public string Evento { get; set; } = string.Empty;

    [Column("entidad")]
    public string Entidad { get; set; } = string.Empty;

    [Column("datos")]
    public string? Datos { get; set; }

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
