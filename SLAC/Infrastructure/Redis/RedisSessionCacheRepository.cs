using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using StackExchange.Redis;

namespace SLAC.Infrastructure.Redis;

public class RedisSessionCacheRepository : ISessionCacheRepository
{
    private readonly RedisConnectionProvider _connectionProvider;
    private readonly ILogger<RedisSessionCacheRepository> _logger;

    // Caché en memoria de respaldo para degradación elegante en desarrollo (SRS 3.3)
    private static readonly ConcurrentDictionary<Guid, (SessionEphemeralState State, DateTimeOffset ExpiresAt)> _fallbackCache = new();

    public RedisSessionCacheRepository(
        RedisConnectionProvider connectionProvider,
        ILogger<RedisSessionCacheRepository> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    private static string GetKey(Guid sesionId) => $"slac:sesion:{sesionId}";

    public async Task SetActiveSessionAsync(SessionEphemeralState state, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var db = _connectionProvider.GetDatabase();
        var key = GetKey(state.SesionId);
        var json = JsonSerializer.Serialize(state);

        if (db != null)
        {
            try
            {
                await db.StringSetAsync(key, json, ttl);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al persistir sesión en Redis. Usando fallback en memoria.");
            }
        }

        _fallbackCache[state.SesionId] = (state, DateTimeOffset.UtcNow.Add(ttl));
    }

    public async Task<SessionEphemeralState?> GetActiveSessionAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        var db = _connectionProvider.GetDatabase();
        var key = GetKey(sesionId);

        if (db != null)
        {
            try
            {
                var value = await db.StringGetAsync(key);
                if (value.HasValue)
                {
                    return JsonSerializer.Deserialize<SessionEphemeralState>(value.ToString()!);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al leer sesión de Redis. Verificando fallback.");
            }
        }

        if (_fallbackCache.TryGetValue(sesionId, out var entry))
        {
            if (DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                return entry.State;
            }
            _fallbackCache.TryRemove(sesionId, out _);
        }

        return null;
    }

    public async Task<long> IncrementAttendanceCounterAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        var db = _connectionProvider.GetDatabase();
        var counterKey = $"slac:sesion:{sesionId}:contador";

        if (db != null)
        {
            try
            {
                return await db.StringIncrementAsync(counterKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error incrementando contador en Redis.");
            }
        }

        if (_fallbackCache.TryGetValue(sesionId, out var entry))
        {
            entry.State.ContadorAsistentes++;
            return entry.State.ContadorAsistentes;
        }

        return 1;
    }

    public async Task<bool> IsSessionActiveAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        var db = _connectionProvider.GetDatabase();
        var key = GetKey(sesionId);

        if (db != null)
        {
            try
            {
                return await db.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error comprobando existencia en Redis.");
            }
        }

        if (_fallbackCache.TryGetValue(sesionId, out var entry))
        {
            return DateTimeOffset.UtcNow <= entry.ExpiresAt;
        }

        return false;
    }

    public async Task InvalidateSessionAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        var db = _connectionProvider.GetDatabase();
        var key = GetKey(sesionId);
        var counterKey = $"slac:sesion:{sesionId}:contador";

        if (db != null)
        {
            try
            {
                await db.KeyDeleteAsync(new RedisKey[] { key, counterKey });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error eliminando sesión en Redis.");
            }
        }

        _fallbackCache.TryRemove(sesionId, out _);
    }
}
