using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using StackExchange.Redis;

namespace SLAC.Infrastructure.Redis;

public class RedisSessionEventBus : ISessionEventBus
{
    private readonly RedisConnectionProvider _connectionProvider;
    private readonly ILogger<RedisSessionEventBus> _logger;

    // Suscriptores en memoria para pruebas locales o fallback sin Redis activo
    private static readonly ConcurrentDictionary<Guid, List<Func<AttendanceEventMessage, Task>>> _inMemorySubscribers = new();

    public RedisSessionEventBus(
        RedisConnectionProvider connectionProvider,
        ILogger<RedisSessionEventBus> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    private static RedisChannel GetChannel(Guid sesionId) => 
        new RedisChannel($"slac:sesion:{sesionId}:eventos", RedisChannel.PatternMode.Literal);

    public async Task PublishEventAsync(AttendanceEventMessage message, CancellationToken cancellationToken = default)
    {
        var subscriber = _connectionProvider.GetSubscriber();
        var json = JsonSerializer.Serialize(message);

        if (subscriber != null)
        {
            try
            {
                var channel = GetChannel(message.SesionId);
                await subscriber.PublishAsync(channel, json);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error publicando evento en Redis Pub/Sub. Retransmitiendo localmente.");
            }
        }

        // Fallback local en memoria
        if (_inMemorySubscribers.TryGetValue(message.SesionId, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    await handler(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error ejecutando handler de evento en memoria.");
                }
            }
        }
    }

    public async Task SubscribeToSessionEventsAsync(Guid sesionId, Func<AttendanceEventMessage, Task> handler, CancellationToken cancellationToken = default)
    {
        var subscriber = _connectionProvider.GetSubscriber();

        if (subscriber != null)
        {
            try
            {
                var channel = GetChannel(sesionId);
                await subscriber.SubscribeAsync(channel, (ch, msg) =>
                {
                    if (msg.HasValue)
                    {
                        var eventMsg = JsonSerializer.Deserialize<AttendanceEventMessage>(msg.ToString());
                        if (eventMsg != null)
                        {
                            _ = handler(eventMsg);
                        }
                    }
                });
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al suscribirse a canal Redis. Usando canal en memoria.");
            }
        }

        _inMemorySubscribers.AddOrUpdate(
            sesionId, 
            new List<Func<AttendanceEventMessage, Task>> { handler },
            (_, list) => { list.Add(handler); return list; });
    }

    public async Task UnsubscribeFromSessionEventsAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        var subscriber = _connectionProvider.GetSubscriber();

        if (subscriber != null)
        {
            try
            {
                var channel = GetChannel(sesionId);
                await subscriber.UnsubscribeAsync(channel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cancelando suscripción en Redis.");
            }
        }

        _inMemorySubscribers.TryRemove(sesionId, out _);
    }
}
