using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("dispositivo_estudiante")]
public class DispositivoEstudianteDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("dispositivo_id")]
    public Guid DispositivoId { get; set; }

    [Column("estudiante_id")]
    public Guid EstudianteId { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
