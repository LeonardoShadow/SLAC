using Postgrest.Attributes;
using Postgrest.Models;

namespace SLAC.Infrastructure.Data.Models;

[Table("lista_asistencia")]
public class ListaAsistenciaDbModel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("institucion_id")]
    public Guid InstitucionId { get; set; }

    [Column("materia_id")]
    public Guid MateriaId { get; set; }

    [Column("docente_id")]
    public Guid DocenteId { get; set; }

    [Column("espacio_id")]
    public Guid EspacioId { get; set; }

    [Column("fecha")]
    public string Fecha { get; set; } = string.Empty;

    [Column("hora_inicio")]
    public string HoraInicio { get; set; } = string.Empty;

    [Column("hora_cierre")]
    public string? HoraCierre { get; set; }

    [Column("estado")]
    public string Estado { get; set; } = "Programada";

    [Column("url_detalle")]
    public string? UrlDetalle { get; set; }

    [Column("total_suscritos")]
    public int TotalSuscritos { get; set; }

    [Column("total_presentes")]
    public int TotalPresentes { get; set; }

    [Column("total_faltas")]
    public int TotalFaltas { get; set; }

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    [Column("actualizado_en")]
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
}
