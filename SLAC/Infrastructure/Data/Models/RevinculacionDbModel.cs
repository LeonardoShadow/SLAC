using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("revinculacion")]
public class RevinculacionDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("estudiante_id")]
    public Guid EstudianteId { get; set; }

    [Column("materia_id")]
    public Guid MateriaId { get; set; }

    [Column("docente_id")]
    public Guid DocenteId { get; set; }

    [Column("expira_en")]
    public DateTime ExpiraEn { get; set; }

    [Column("usada_en")]
    public DateTime? UsadaEn { get; set; }

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
