using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("dispositivo")]
public class DispositivoDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("jti_hash")]
    public string JtiHash { get; set; } = string.Empty;

    [Column("kid")]
    public string Kid { get; set; } = string.Empty;

    [Column("emitido_en")]
    public DateTime EmitidoEn { get; set; } = DateTime.UtcNow;

    [Column("ultimo_uso_en")]
    public DateTime UltimoUsoEn { get; set; } = DateTime.UtcNow;

    [Column("revocado_en")]
    public DateTime? RevocadoEn { get; set; }

    [Column("agente_resumen")]
    public string? AgenteResumen { get; set; }
}
