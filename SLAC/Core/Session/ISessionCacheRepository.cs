using System;
using System.Threading;
using System.Threading.Tasks;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Session.Models;

namespace SLAC.Core.Session;

/// <summary>
/// Contrato para persistir y validar el estado efímero del QR, bloqueo distribuido de proyección
/// y reclamo atómico de asistencia en Redis o memoria (SRS 4.7, Plan Maestro GPS Alpha).
/// </summary>
public interface ISessionCacheRepository
{
    // Métodos pre-existentes
    Task SetActiveSessionAsync(SessionEphemeralState state, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<SessionEphemeralState?> GetActiveSessionAsync(Guid sesionId, CancellationToken cancellationToken = default);
    Task<long> IncrementAttendanceCounterAsync(Guid sesionId, CancellationToken cancellationToken = default);
    Task<bool> IsSessionActiveAsync(Guid sesionId, CancellationToken cancellationToken = default);
    Task InvalidateSessionAsync(Guid sesionId, CancellationToken cancellationToken = default);

    // --- MÉTODOS M1: BLOQUEO DE PROYECCIÓN DISTRIBUIDO ---

    /// <summary>
    /// Intenta adquirir el bloqueo exclusivo de proyección para la sesión.
    /// Es reentrante para el mismo leaseId y renueva el TTL si ya le pertenece.
    /// </summary>
    /// <param name="sesionId">Identificador único de la sesión de clase.</param>
    /// <param name="leaseId">Token o identificador único del circuito/pestaña que proyecta.</param>
    /// <param name="ttl">Tiempo de vida del arrendamiento (e.g. 30 segundos).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>True si el bloqueo fue adquirido o extendido por el mismo titular; False si está tomado por otra ventana.</returns>
    Task<bool> TryAcquireProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Intenta adquirir el bloqueo exclusivo de proyección para la sesión, con opción de toma forzada (force takeover).
    /// </summary>
    Task<bool> TryAcquireProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, bool force, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresca el arrendamiento del bloqueo de proyección únicamente si el leaseId coincide con el titular actual.
    /// </summary>
    /// <param name="sesionId">Identificador único de la sesión de clase.</param>
    /// <param name="leaseId">Token único del titular del arrendamiento.</param>
    /// <param name="ttl">Tiempo de vida a renovar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>True si se renovó exitosamente; False si el bloqueo expiró o pertenece a otro titular.</returns>
    Task<bool> RefreshProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Libera de forma segura el bloqueo de proyección solo si el leaseId coincide con el titular actual.
    /// </summary>
    /// <param name="sesionId">Identificador único de la sesión de clase.</param>
    /// <param name="leaseId">Token único del titular que solicita la liberación.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task ReleaseProjectionLockAsync(Guid sesionId, string leaseId, CancellationToken cancellationToken = default);

    // --- MÉTODOS M1: RECLAMO ATÓMICO Y ROLLBACK DE ASISTENCIA ---

    /// <summary>
    /// Ejecuta el reclamo atómico de asistencia en Redis mediante Lua o doble concurrente en memoria:
    /// Valida estado activo, código visual de 3 dígitos, deduplicación de estudiante y regla de dispositivo RN-05.
    /// </summary>
    /// <param name="sesionId">Identificador de la sesión.</param>
    /// <param name="estudianteId">Identificador del estudiante.</param>
    /// <param name="deviceHash">Hash del dispositivo para RN-05 (opcional o normalizado).</param>
    /// <param name="presenceCodeSubmitted">Código visual de 3 dígitos ingresado por el alumno.</param>
    /// <param name="validatePresenceCode">Indica si se exige validar el código visual (por defecto true).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Resultado tipado del reclamo con estado, contador en vivo y mensaje de error si aplica.</returns>
    Task<AttendanceClaimResult> TryClaimAttendanceAsync(
        Guid sesionId,
        Guid estudianteId,
        string? deviceHash,
        string? presenceCodeSubmitted,
        bool validatePresenceCode = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta la compensación transaccional: elimina el registro efímero del estudiante y del dispositivo,
    /// y decrementa el contador en vivo únicamente si el registro existía.
    /// </summary>
    /// <param name="sesionId">Identificador de la sesión.</param>
    /// <param name="estudianteId">Identificador del estudiante.</param>
    /// <param name="deviceHash">Hash del dispositivo a liberar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task RollbackAttendanceClaimAsync(
        Guid sesionId,
        Guid estudianteId,
        string? deviceHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reinicia todos los reclamos y contadores efímeros de asistencia para una sesión específica,
    /// permitiendo repetir pruebas de escaneo en la misma sesión sin colisión de claves.
    /// </summary>
    /// <param name="sesionId">Identificador único de la sesión.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task ResetAttendanceClaimsAsync(Guid sesionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
