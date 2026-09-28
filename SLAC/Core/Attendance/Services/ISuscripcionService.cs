namespace SLAC.Core.Attendance.Services;

/// <summary>
/// Contrato del servicio de suscripción y registro de asistencia (Paso 30).
/// Valida la sesión QR, gestiona la identidad anónima del estudiante, suscripción a la materia,
/// cálculo de faltas retroactivas e inserción idempotente de asistencia presente.
/// </summary>
public interface ISuscripcionService
{
    Task<AttendanceScanResult> ProcesarEscaneoAsync(AttendanceScanRequest request, CancellationToken ct = default);
}
