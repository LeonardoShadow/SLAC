using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("estudiante")]
public class EstudianteDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [Column("nombres")]
    public string Nombres { get; set; } = string.Empty;

    [Column("apellidos")]
    public string Apellidos { get; set; } = string.Empty;

    [Column("correo")]
    public string Correo { get; set; } = string.Empty;

    [Column("consentimiento_en")]
    public DateTime? ConsentimientoEn { get; set; }

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
