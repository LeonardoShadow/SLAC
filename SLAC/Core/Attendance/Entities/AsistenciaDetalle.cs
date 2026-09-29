namespace SLAC.Core.Attendance.Entities;

/// <summary>
/// Representa el registro de asistencia individual de un estudiante en una sesión.
/// Estados: 'Presente', 'Falta'.
/// Orígenes: 'QR', 'Cierre', 'Suscripción tardía', 'Corrección'.
/// </summary>
public class AsistenciaDetalle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public Guid ListaId { get; set; }
    public Guid EstudianteId { get; set; }
    public string Estado { get; set; } = "Presente";
    public string Origen { get; set; } = "QR";
    public DateTimeOffset? HoraLlegada { get; set; }
    public int? MinutosDesdeInicio { get; set; }
    public Guid? DispositivoId { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public double? PrecisionGps { get; set; }
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
