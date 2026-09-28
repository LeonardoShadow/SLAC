using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("asistencia_detalle")]
public class AsistenciaDetalleDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("lista_id")]
    public Guid ListaId { get; set; }

    [Column("estudiante_id")]
    public Guid EstudianteId { get; set; }

    [Column("estado")]
    public string Estado { get; set; } = "Presente";

    [Column("origen")]
    public string Origen { get; set; } = "QR";

    [Column("hora_llegada")]
    public DateTime? HoraLlegada { get; set; }

    [Column("minutos_desde_inicio")]
    public int? MinutosDesdeInicio { get; set; }

    [Column("dispositivo_id")]
    public Guid? DispositivoId { get; set; }

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}
