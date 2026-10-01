namespace SLAC.Core.Attendance.Services;

public class AttendanceScanRequest
{
    public Guid SesionId { get; set; }
    public string QrToken { get; set; } = string.Empty;
    public string? DeviceToken { get; set; }

    // Datos para vinculación inicial de dispositivo (Código o Correo Institucional + CI/PIN)
    public string? Identificador { get; set; }
    public string? DocumentoIdentidad { get; set; }

    // Datos de retrocompatibilidad
    public string? Codigo { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? Correo { get; set; }
    public bool AceptaTerminos { get; set; } = true;
    public string? UserAgent { get; set; }

    // Geolocalización GPS y Modo de Validación ("gps" o "wifi")
    public string Modo { get; set; } = "gps";
    public bool RequiereGps => !string.Equals(Modo, "wifi", StringComparison.OrdinalIgnoreCase);
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public double? PrecisionGpsMetros { get; set; }
    public int? RadioToleranciaPersonalizado { get; set; }
    public double? LatitudReferencia { get; set; }
    public double? LongitudReferencia { get; set; }
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

    public Guid? SolicitudRevinculacionId { get; set; }

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
        string? materiaCodigo = null,
        Guid? solicitudRevinculacionId = null,
        Guid? estudianteId = null) => new()
    {
        Exito = false,
        RequiereRevinculacion = true,
        MensajeRevinculacion = mensaje,
        EstudianteNombre = estudianteNombre,
        EstudianteCodigo = estudianteCodigo,
        MateriaNombre = materiaNombre,
        MateriaCodigo = materiaCodigo,
        SolicitudRevinculacionId = solicitudRevinculacionId,
        EstudianteId = estudianteId
    };
}
