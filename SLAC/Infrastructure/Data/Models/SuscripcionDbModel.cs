using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("suscripcion")]
public class SuscripcionDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("materia_id")]
    public Guid MateriaId { get; set; }

    [Column("estudiante_id")]
    public Guid EstudianteId { get; set; }

    [Column("fecha")]
    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    [Column("lista_origen_id")]
    public Guid? ListaOrigenId { get; set; }

    [Column("estado")]
    public string Estado { get; set; } = "activa";

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
