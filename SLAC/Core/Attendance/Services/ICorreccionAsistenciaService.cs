namespace SLAC.Core.Attendance.Services;

/// <summary>
/// Contrato de servicio para correcciones manuales y suspensión de emergencia de clases
/// con auditoría inmutable obligatoria (Pasos 39 y 40).
/// </summary>
public interface ICorreccionAsistenciaService
{
    /// <summary>
    /// Modifica manualmente el estado de asistencia de un estudiante (ej. Falta -> Presente por justificación),
    /// actualiza los totales de la sesión y emite un registro inmutable en auditoria.
    /// </summary>
    Task<(bool Exito, string? Error)> CorregirAsistenciaAsync(
        Guid listaId,
        Guid estudianteId,
        string nuevoEstado,
        string motivo,
        string actorDocente,
        CancellationToken ct = default);

    /// <summary>
    /// Marca de emergencia una sesión como 'Suspendida', anula el código QR,
    /// previene la generación de faltas y registra el evento en auditoria.
    /// </summary>
    Task<(bool Exito, string? Error)> SuspenderClaseEmergenciaAsync(
        Guid listaId,
        string motivo,
        string actorDocente,
        CancellationToken ct = default);
}
