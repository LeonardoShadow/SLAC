using SLAC.Core.Session.Models;

namespace SLAC.Core.Session;

/// <summary>
/// Contrato para persistir y validar el estado efímero del QR en Redis con expiración automática (SRS 4.7).
/// </summary>
public interface ISessionCacheRepository
{
    /// <summary>
    /// Guarda o actualiza el estado de la sesión activa con TTL.
    /// </summary>
    Task SetActiveSessionAsync(SessionEphemeralState state, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el estado actual de la sesión desde la clave efímera 'slac:sesion:{id}'.
    /// </summary>
    Task<SessionEphemeralState?> GetActiveSessionAsync(Guid sesionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementa de forma atómica el contador de asistentes en vivo en Redis.
    /// </summary>
    Task<long> IncrementAttendanceCounterAsync(Guid sesionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica si la sesión existe y su ventana está activa sin deserializar todo el estado.
    /// </summary>
    Task<bool> IsSessionActiveAsync(Guid sesionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalida y elimina la clave de la sesión al cierre (hora de inicio + 20 min).
    /// </summary>
    Task InvalidateSessionAsync(Guid sesionId, CancellationToken cancellationToken = default);
}
