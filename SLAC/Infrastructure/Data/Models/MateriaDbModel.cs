using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("materia")]
public class MateriaDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("docente_id")]
    public Guid DocenteId { get; set; }

    [Column("periodo_id")]
    public Guid PeriodoId { get; set; }

    [Column("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Column("grupo")]
    public string Grupo { get; set; } = string.Empty;

    [Column("espacio_id")]
    public Guid EspacioId { get; set; }

    [Column("hora_inicio")]
    public string HoraInicio { get; set; } = "08:00:00";

    [Column("dias")]
    public string Dias { get; set; } = "L,M,V";

    [Column("estado")]
    public string Estado { get; set; } = "activa";

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
