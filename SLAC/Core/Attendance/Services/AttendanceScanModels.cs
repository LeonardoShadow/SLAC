namespace SLAC.Core.Attendance.Services;

public class AttendanceScanRequest
{
    public Guid SesionId { get; set; }
    public string QrToken { get; set; } = string.Empty;
    public string? DeviceToken { get; set; }

    // Datos para formulario de estudiante nuevo (si no cuenta con credencial válida)
    public string? Codigo { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? Correo { get; set; }
    public bool AceptaTerminos { get; set; }
    public string? UserAgent { get; set; }
}

public class AttendanceScanResult
{
    public bool Exito { get; set; }
    public string? MensajeError { get; set; }
    public bool RequiereRegistro { get; set; }
    public bool RequiereRevinculacion { get; set; }
    public string? MensajeRevinculacion { get; set; }
    public bool YaRegistradoHoy { get; set; }

    public Guid? EstudianteId { get; set; }
    public string? EstudianteNombre { get; set; }
    public string? EstudianteCodigo { get; set; }
    public string? MateriaNombre { get; set; }
    public string? MateriaCodigo { get; set; }
    public DateTimeOffset? HoraLlegada { get; set; }
    public int? MinutosDesdeInicio { get; set; }

    /// <summary>
    /// Cadena del JWT firmado (ES256) que el controlador debe inyectar como cookie HttpOnly en la respuesta.
    /// </summary>
    public string? NuevaDeviceCookie { get; set; }

    public static AttendanceScanResult Error(string mensaje) => new()
    {
        Exito = false,
        MensajeError = mensaje
    };

    public static AttendanceScanResult RegistroRequerido(string? materiaNombre = null, string? materiaCodigo = null) => new()
    {
        Exito = false,
        RequiereRegistro = true,
        MateriaNombre = materiaNombre,
        MateriaCodigo = materiaCodigo
    };

    public static AttendanceScanResult RevinculacionRequerida(
        string mensaje,
        string? estudianteNombre = null,
        string? estudianteCodigo = null,
        string? materiaNombre = null,
        string? materiaCodigo = null) => new()
    {
        Exito = false,
        RequiereRevinculacion = true,
        MensajeRevinculacion = mensaje,
        EstudianteNombre = estudianteNombre,
        EstudianteCodigo = estudianteCodigo,
        MateriaNombre = materiaNombre,
        MateriaCodigo = materiaCodigo
    };
}
