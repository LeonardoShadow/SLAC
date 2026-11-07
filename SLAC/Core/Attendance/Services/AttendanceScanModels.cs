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

    // Geolocalización GPS y Modo de Validación ("gps", "wifi" o "proximidad")
    public string Modo { get; set; } = "gps";
    public bool RequiereGps => !string.Equals(Modo, "wifi", StringComparison.OrdinalIgnoreCase);
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public double? PrecisionGpsMetros { get; set; }
    public int? RadioToleranciaPersonalizado { get; set; }
    public double? LatitudReferencia { get; set; }
    public double? LongitudReferencia { get; set; }

    // Reto visual dinámico de 3 dígitos proyectado en pantalla (Modo GPS Alpha)
    public string? DynamicPresenceCode { get; set; }

    // Capa 2: Fusión Inercial Web y Zero-Velocity Update (ZUPT)
    public bool? IsStationary { get; set; }
    public double? AccelerationVariance { get; set; }

    // Serie temporal cruda obligatoria para certificación Zero-Trust
    public List<RawGpsTelemetrySample>? Samples { get; set; }
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

    // Métricas de validación por Proximidad / GPS
    public double? DistanciaCalculadaMetros { get; set; }
    public double? RadioPermitidoMetros { get; set; }
    public string? ModoValidacionAplicado { get; set; }
    public double? LatitudEstudiante { get; set; }
    public double? LongitudEstudiante { get; set; }
    public double? PrecisionGpsMetros { get; set; }
    public double? LatitudReferencia { get; set; }
    public double? LongitudReferencia { get; set; }

    // Capa 3: Indicadores de compensación D-GPS Relativo y ZUPT
    public bool? IsStationaryCertified { get; set; }
    public bool? RelativeDifferentialApplied { get; set; }

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

/// <summary>
/// Muestra telemétrica cruda individual emitida por el hardware GNSS del dispositivo cliente.
/// </summary>
public class RawGpsTelemetrySample
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Acc { get; set; }
    public double? Speed { get; set; }
    public long TimestampMs { get; set; }
    public bool? IsStationary { get; set; }
    public double? AccelerationVariance { get; set; }
}

/// <summary>
/// Estados de resultado para la reserva y deduplicación atómica de asistencia en Redis (LUA_CLAIM_ATTENDANCE).
/// </summary>
public enum AttendanceClaimStatus
{
    Success = 0,
    AlreadyRegistered = 1,
    InvalidPresenceCode = -1,
    DeviceConflict = -2,
    SessionInactive = -3,
    DegradedModeFallback = 2
}

/// <summary>
/// Resultado inmutable de la solicitud atómica de reserva de asistencia.
/// </summary>
public sealed record AttendanceClaimResult(
    AttendanceClaimStatus Status,
    long LiveAttendanceCount,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status is AttendanceClaimStatus.Success or AttendanceClaimStatus.DegradedModeFallback;
    public bool IsAlreadyRegistered => Status == AttendanceClaimStatus.AlreadyRegistered;
    public bool IsRejected => (int)Status < 0;

    public static AttendanceClaimResult Ok(long count) =>
        new(AttendanceClaimStatus.Success, count);

    public static AttendanceClaimResult AlreadyDone(long count) =>
        new(AttendanceClaimStatus.AlreadyRegistered, count, "Asistencia ya registrada previamente.");

    public static AttendanceClaimResult Rejected(AttendanceClaimStatus status, string message) =>
        new(status, 0, message);

    public static AttendanceClaimResult FallbackOk(long count) =>
        new(AttendanceClaimStatus.DegradedModeFallback, count);
}

/// <summary>
/// Resultado del proceso de auditoría y certificación telemétrica en el servidor (GeoTelemetryValidator).
/// </summary>
public class TelemetryValidationResult
{
    public bool IsValid { get; set; }
    public string? RejectReason { get; set; }
    public double VerifiedLatitude { get; set; }
    public double VerifiedLongitude { get; set; }
    public double VerifiedAccuracyMetros { get; set; }
    public double VerifiedVariance { get; set; }
    public double SpatialStdDevMetros { get; set; }
    public double MaxKinematicVelocityMps { get; set; }
    public int RetainedSampleCount { get; set; }

    // Capa 2 y 3: Certificación inercial ZUPT y corrección diferencial relativa D-GPS
    public bool IsStationaryCertified { get; set; }
    public double? InertialAccelerationVariance { get; set; }
    public bool RelativeDifferentialApplied { get; set; }
    public double? RelativeOffsetMeters { get; set; }

    // Aliases semánticos para interoperabilidad y contratos de alto nivel
    public double CertifiedLat => VerifiedLatitude;
    public double CertifiedLon => VerifiedLongitude;
    public double CertifiedAcc => VerifiedAccuracyMetros;
}

/// <summary>
/// Resultado del proceso de compensación diferencial relativa (D-GPS Relativo) en servidor.
/// </summary>
public sealed class RelativeDifferentialCorrectionResult
{
    public bool IsApplied { get; set; }
    public double CorrectedStudentLat { get; set; }
    public double CorrectedStudentLon { get; set; }
    public double DeltaLatBias { get; set; }
    public double DeltaLonBias { get; set; }
    public double BiasMagnitudeMeters { get; set; }
    public double RelativeDistanceMeters { get; set; }
    public string? StatusMessage { get; set; }
}
