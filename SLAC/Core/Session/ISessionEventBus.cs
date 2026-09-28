using SLAC.Core.Session.Models;

namespace SLAC.Core.Session;

/// <summary>
/// Contrato para publicación y suscripción de eventos en tiempo real mediante Redis Pub/Sub (SRS 4.7).
/// </summary>
public interface ISessionEventBus
{
    /// <summary>
    /// Publica un evento de sesión al canal 'slac:sesion:{id}:eventos'.
    /// </summary>
    Task PublishEventAsync(AttendanceEventMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Se suscribe al canal de eventos de una sesión para retransmitir a la pantalla del docente.
    /// </summary>
    Task SubscribeToSessionEventsAsync(Guid sesionId, Func<AttendanceEventMessage, Task> handler, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela la suscripción al canal de eventos de la sesión.
    /// </summary>
    Task UnsubscribeFromSessionEventsAsync(Guid sesionId, CancellationToken cancellationToken = default);
}
