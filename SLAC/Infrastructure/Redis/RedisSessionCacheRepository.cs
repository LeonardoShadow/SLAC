using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using StackExchange.Redis;

namespace SLAC.Infrastructure.Redis;

public class RedisSessionCacheRepository : ISessionCacheRepository
{
    private readonly RedisConnectionProvider _connectionProvider;
    private readonly ILogger<RedisSessionCacheRepository> _logger;

    // Cachés en memoria de respaldo para degradación elegante y testabilidad offline (SRS 3.3)
    private static readonly ConcurrentDictionary<Guid, (SessionEphemeralState State, DateTimeOffset ExpiresAt)> _fallbackCache = new();
    private static readonly ConcurrentDictionary<Guid, (string LeaseId, DateTimeOffset ExpiresAt)> _fallbackProjectionLocks = new();
    private static readonly ConcurrentDictionary<string, byte> _fallbackAttendanceClaims = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Guid> _fallbackDeviceClaims = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Guid, long> _fallbackCounters = new();
    private static readonly ConcurrentDictionary<Guid, object> _fallbackSessionLocks = new();

    // Scripts Lua atómicos
    private const string LuaAcquireProjectionLock = @"
local current = redis.call('GET', @lockKey)
if @force == '1' or not current or current == @leaseId then
    redis.call('SET', @lockKey, @leaseId, 'EX', @ttlSeconds)
    return 1
else
    return 0
end";

    private const string LuaRefreshProjectionLock = @"
if redis.call('GET', @lockKey) == @leaseId then
    redis.call('EXPIRE', @lockKey, @ttlSeconds)
    return 1
else
    return 0
end";

    private const string LuaReleaseProjectionLock = @"
if redis.call('GET', @lockKey) == @leaseId then
    return redis.call('DEL', @lockKey)
else
    return 0
end";

    private const string LuaClaimAttendance = @"
local sessionJson = redis.call('GET', @sessionKey)
if not sessionJson then return { -3, 0 } end

if @validateCode == '1' then
    local pattern = '""[Cc]odigo[Pp]resencia[Aa]ctual""%s*:%s*""([^""]+)""'
    local activeCode = string.match(sessionJson, pattern)
    if activeCode and activeCode ~= '' and activeCode ~= @presenceCode then
        return { -1, 0 }
    end
end

local studentExists = redis.call('EXISTS', @studentKey)
if studentExists == 1 then
    local currentCount = redis.call('GET', @counterKey)
    return { 1, tonumber(currentCount) or 0 }
end

if @hasDevice == '1' then
    local deviceOwner = redis.call('GET', @deviceKey)
    if deviceOwner and deviceOwner ~= @estudianteId then
        return { -2, 0 }
    end
end

redis.call('SET', @studentKey, '1', 'EX', @ttl)
if @hasDevice == '1' then
    redis.call('SET', @deviceKey, @estudianteId, 'EX', @ttl)
end

local newCount = redis.call('INCR', @counterKey)
return { 0, newCount }";

    private const string LuaRollbackAttendance = @"
local deleted = redis.call('DEL', @studentKey)
if deleted == 1 then
    if @hasDevice == '1' then
        local currentOwner = redis.call('GET', @deviceKey)
        if currentOwner == @estudianteId then
            redis.call('DEL', @deviceKey)
        end
    end
    local c = redis.call('DECR', @counterKey)
    if c < 0 then
        redis.call('SET', @counterKey, 0)
    end
    return 1
else
    return 0
end";

    private static readonly LuaScript _prepAcquireProjectionLock = LuaScript.Prepare(LuaAcquireProjectionLock);
    private static readonly LuaScript _prepRefreshProjectionLock = LuaScript.Prepare(LuaRefreshProjectionLock);
    private static readonly LuaScript _prepReleaseProjectionLock = LuaScript.Prepare(LuaReleaseProjectionLock);
    private static readonly LuaScript _prepClaimAttendance = LuaScript.Prepare(LuaClaimAttendance);
    private static readonly LuaScript _prepRollbackAttendance = LuaScript.Prepare(LuaRollbackAttendance);

    public RedisSessionCacheRepository(
        RedisConnectionProvider connectionProvider,
        ILogger<RedisSessionCacheRepository> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    private static string GetKey(Guid sesionId) => $"slac:sesion:{sesionId}";
    private static string GetLockKey(Guid sesionId) => $"slac:sesion:{sesionId}:projection_lock";
    private static string? NormalizeDeviceHash(string? deviceHash) =>
        string.IsNullOrWhiteSpace(deviceHash) ? null : deviceHash.Trim().ToLowerInvariant();

    public async Task SetActiveSessionAsync(SessionEphemeralState state, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
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
            _fallbackCounters[sesionId] = entry.State.ContadorAsistentes;
            return entry.State.ContadorAsistentes;
        }

        return _fallbackCounters.AddOrUpdate(sesionId, 1, (_, c) => c + 1);
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
        var lockKey = GetLockKey(sesionId);

        if (db != null)
        {
            try
            {
                await db.KeyDeleteAsync(new RedisKey[] { key, counterKey, lockKey });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error eliminando sesión en Redis.");
            }
        }

        _fallbackCache.TryRemove(sesionId, out _);
        _fallbackProjectionLocks.TryRemove(sesionId, out _);
        _fallbackCounters.TryRemove(sesionId, out _);
        _fallbackSessionLocks.TryRemove(sesionId, out _);
    }

    // --- BLOQUEO DE PROYECCIÓN DISTRIBUIDO ---

    public Task<bool> TryAcquireProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, CancellationToken cancellationToken = default)
        => TryAcquireProjectionLockAsync(sesionId, leaseId, ttl, force: false, cancellationToken);

    public async Task<bool> TryAcquireProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, bool force, CancellationToken cancellationToken = default)
    {
        if (sesionId == Guid.Empty) throw new ArgumentException("El ID de sesión no puede ser Guid.Empty.", nameof(sesionId));
        if (string.IsNullOrWhiteSpace(leaseId)) throw new ArgumentException("El leaseId no puede ser nulo o vacío.", nameof(leaseId));
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl), "El TTL debe ser positivo.");

        var db = _connectionProvider.GetDatabase();
        var lockKey = GetLockKey(sesionId);

        if (db != null)
        {
            try
            {
                var result = await _prepAcquireProjectionLock.EvaluateAsync(db, new
                {
                    lockKey = (RedisKey)lockKey,
                    leaseId = (RedisValue)leaseId,
                    ttlSeconds = (int)ttl.TotalSeconds,
                    force = (RedisValue)(force ? "1" : "0")
                });

                return (int)result == 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error adquiriendo bloqueo de proyección en Redis. Usando fallback en memoria.");
            }
        }

        return TryAcquireProjectionLockInMemory(sesionId, leaseId, ttl, force);
    }

    public async Task<bool> RefreshProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (sesionId == Guid.Empty) throw new ArgumentException("El ID de sesión no puede ser Guid.Empty.", nameof(sesionId));
        if (string.IsNullOrWhiteSpace(leaseId)) throw new ArgumentException("El leaseId no puede ser nulo o vacío.", nameof(leaseId));
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl), "El TTL debe ser positivo.");

        var db = _connectionProvider.GetDatabase();
        var lockKey = GetLockKey(sesionId);

        if (db != null)
        {
            try
            {
                var result = await _prepRefreshProjectionLock.EvaluateAsync(db, new
                {
                    lockKey = (RedisKey)lockKey,
                    leaseId = (RedisValue)leaseId,
                    ttlSeconds = (int)ttl.TotalSeconds
                });

                return (int)result == 1;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error refrescando bloqueo de proyección en Redis. Usando fallback en memoria.");
            }
        }

        return RefreshProjectionLockInMemory(sesionId, leaseId, ttl);
    }

    public async Task ReleaseProjectionLockAsync(Guid sesionId, string leaseId, CancellationToken cancellationToken = default)
    {
        if (sesionId == Guid.Empty) return;
        if (string.IsNullOrWhiteSpace(leaseId)) return;

        var db = _connectionProvider.GetDatabase();
        var lockKey = GetLockKey(sesionId);

        if (db != null)
        {
            try
            {
                await _prepReleaseProjectionLock.EvaluateAsync(db, new
                {
                    lockKey = (RedisKey)lockKey,
                    leaseId = (RedisValue)leaseId
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error liberando bloqueo de proyección en Redis. Usando fallback en memoria.");
            }
        }

        ReleaseProjectionLockInMemory(sesionId, leaseId);
    }

    // --- RECLAMO ATÓMICO Y ROLLBACK DE ASISTENCIA ---

    public async Task<AttendanceClaimResult> TryClaimAttendanceAsync(
        Guid sesionId,
        Guid estudianteId,
        string? deviceHash,
        string? presenceCodeSubmitted,
        bool validatePresenceCode = true,
        CancellationToken cancellationToken = default)
    {
        if (sesionId == Guid.Empty) throw new ArgumentException("El ID de sesión no puede ser Guid.Empty.", nameof(sesionId));
        if (estudianteId == Guid.Empty) throw new ArgumentException("El ID de estudiante no puede ser Guid.Empty.", nameof(estudianteId));

        var cleanDevice = NormalizeDeviceHash(deviceHash);
        var db = _connectionProvider.GetDatabase();

        if (db != null)
        {
            try
            {
                var sessionKey = GetKey(sesionId);
                var studentKey = $"slac:sesion:{sesionId}:asistencia:{estudianteId}";
                var deviceKey = $"slac:sesion:{sesionId}:device:{cleanDevice ?? "none"}";
                var counterKey = $"slac:sesion:{sesionId}:contador";

                var result = (RedisResult[]?)await _prepClaimAttendance.EvaluateAsync(db, new
                {
                    sessionKey = (RedisKey)sessionKey,
                    studentKey = (RedisKey)studentKey,
                    deviceKey = (RedisKey)deviceKey,
                    counterKey = (RedisKey)counterKey,
                    validateCode = (RedisValue)(validatePresenceCode ? "1" : "0"),
                    presenceCode = (RedisValue)(presenceCodeSubmitted?.Trim() ?? ""),
                    hasDevice = (RedisValue)(cleanDevice != null ? "1" : "0"),
                    estudianteId = (RedisValue)estudianteId.ToString(),
                    ttl = 86400
                });

                if (result != null && result.Length >= 2)
                {
                    var statusCode = (int)result[0];
                    var currentCount = (long)result[1];

                    return statusCode switch
                    {
                        0 => AttendanceClaimResult.Ok(currentCount),
                        1 => AttendanceClaimResult.AlreadyDone(currentCount),
                        -1 => AttendanceClaimResult.Rejected(AttendanceClaimStatus.InvalidPresenceCode, "Código de presencia visual incorrecto o expirado."),
                        -2 => AttendanceClaimResult.Rejected(AttendanceClaimStatus.DeviceConflict, "Este dispositivo ya registró asistencia para otro alumno en esta sesión (RN-05)."),
                        -3 => AttendanceClaimResult.Rejected(AttendanceClaimStatus.SessionInactive, "La sesión no existe o está inactiva."),
                        _ => AttendanceClaimResult.Rejected(AttendanceClaimStatus.SessionInactive, "Error inesperado en evaluación de asistencia.")
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error evaluando reclamo de asistencia en Redis. Usando fallback en memoria.");
            }
        }

        return TryClaimAttendanceInMemory(sesionId, estudianteId, cleanDevice, presenceCodeSubmitted, validatePresenceCode);
    }

    public async Task RollbackAttendanceClaimAsync(
        Guid sesionId,
        Guid estudianteId,
        string? deviceHash,
        CancellationToken cancellationToken = default)
    {
        if (sesionId == Guid.Empty || estudianteId == Guid.Empty) return;

        var cleanDevice = NormalizeDeviceHash(deviceHash);
        var db = _connectionProvider.GetDatabase();

        if (db != null)
        {
            try
            {
                var studentKey = $"slac:sesion:{sesionId}:asistencia:{estudianteId}";
                var deviceKey = $"slac:sesion:{sesionId}:device:{cleanDevice ?? "none"}";
                var counterKey = $"slac:sesion:{sesionId}:contador";

                await _prepRollbackAttendance.EvaluateAsync(db, new
                {
                    studentKey = (RedisKey)studentKey,
                    deviceKey = (RedisKey)deviceKey,
                    counterKey = (RedisKey)counterKey,
                    hasDevice = (RedisValue)(cleanDevice != null ? "1" : "0"),
                    estudianteId = (RedisValue)estudianteId.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error ejecutando rollback de asistencia en Redis. Usando fallback en memoria.");
            }
        }

        RollbackAttendanceClaimInMemory(sesionId, estudianteId, cleanDevice);
    }

    // --- MÉTODOS PRIVADOS DE RESPALDO EN MEMORIA CONCURRENTE ---

    private static bool TryAcquireProjectionLockInMemory(Guid sesionId, string leaseId, TimeSpan ttl, bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        var newExpiry = now.Add(ttl);

        while (true)
        {
            if (_fallbackProjectionLocks.TryGetValue(sesionId, out var existing))
            {
                if (!force && existing.ExpiresAt > now && !string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
                {
                    return false; // Bloqueo activo en manos de otra ventana
                }

                if (_fallbackProjectionLocks.TryUpdate(sesionId, (leaseId, newExpiry), existing))
                {
                    return true;
                }
            }
            else
            {
                if (_fallbackProjectionLocks.TryAdd(sesionId, (leaseId, newExpiry)))
                {
                    return true;
                }
            }
        }
    }

    private static bool RefreshProjectionLockInMemory(Guid sesionId, string leaseId, TimeSpan ttl)
    {
        var now = DateTimeOffset.UtcNow;
        if (_fallbackProjectionLocks.TryGetValue(sesionId, out var existing))
        {
            if (existing.ExpiresAt > now && string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
            {
                var newExpiry = now.Add(ttl);
                return _fallbackProjectionLocks.TryUpdate(sesionId, (leaseId, newExpiry), existing);
            }
        }
        return false;
    }

    private static void ReleaseProjectionLockInMemory(Guid sesionId, string leaseId)
    {
        if (_fallbackProjectionLocks.TryGetValue(sesionId, out var existing))
        {
            if (string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
            {
                _fallbackProjectionLocks.TryRemove(sesionId, out _);
            }
        }
    }

    private static AttendanceClaimResult TryClaimAttendanceInMemory(
        Guid sesionId, Guid estudianteId, string? cleanDeviceHash, string? presenceCodeSubmitted, bool validatePresenceCode)
    {
        var syncRoot = _fallbackSessionLocks.GetOrAdd(sesionId, _ => new object());
        lock (syncRoot)
        {
            if (!_fallbackCache.TryGetValue(sesionId, out var entry) || DateTimeOffset.UtcNow > entry.ExpiresAt)
            {
                return AttendanceClaimResult.Rejected(AttendanceClaimStatus.SessionInactive, "La sesión no existe o está inactiva.");
            }

            if (validatePresenceCode && !string.IsNullOrWhiteSpace(entry.State.CodigoPresenciaActual))
            {
                if (!string.Equals(entry.State.CodigoPresenciaActual.Trim(), presenceCodeSubmitted?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return AttendanceClaimResult.Rejected(AttendanceClaimStatus.InvalidPresenceCode, "Código de presencia visual incorrecto o expirado.");
                }
            }

            var studentKey = $"{sesionId:N}:{estudianteId:N}";
            if (_fallbackAttendanceClaims.ContainsKey(studentKey))
            {
                var curCount = _fallbackCounters.GetOrAdd(sesionId, entry.State.ContadorAsistentes);
                return AttendanceClaimResult.AlreadyDone(curCount);
            }

            if (!string.IsNullOrWhiteSpace(cleanDeviceHash))
            {
                var deviceKey = $"{sesionId:N}:{cleanDeviceHash}";
                if (_fallbackDeviceClaims.TryGetValue(deviceKey, out var existingOwner) && existingOwner != estudianteId)
                {
                    return AttendanceClaimResult.Rejected(AttendanceClaimStatus.DeviceConflict, "Este dispositivo ya registró asistencia para otro alumno en esta sesión (RN-05).");
                }
            }

            _fallbackAttendanceClaims[studentKey] = 1;

            if (!string.IsNullOrWhiteSpace(cleanDeviceHash))
            {
                var deviceKey = $"{sesionId:N}:{cleanDeviceHash}";
                _fallbackDeviceClaims[deviceKey] = estudianteId;
            }

            long newCount = _fallbackCounters.AddOrUpdate(sesionId, _ => (long)entry.State.ContadorAsistentes + 1, (_, c) => c + 1);
            entry.State.ContadorAsistentes = (int)newCount;

            return AttendanceClaimResult.Ok(newCount);
        }
    }

    private static void RollbackAttendanceClaimInMemory(Guid sesionId, Guid estudianteId, string? cleanDeviceHash)
    {
        var syncRoot = _fallbackSessionLocks.GetOrAdd(sesionId, _ => new object());
        lock (syncRoot)
        {
            var studentKey = $"{sesionId:N}:{estudianteId:N}";
            if (_fallbackAttendanceClaims.TryRemove(studentKey, out _))
            {
                if (!string.IsNullOrWhiteSpace(cleanDeviceHash))
                {
                    var deviceKey = $"{sesionId:N}:{cleanDeviceHash}";
                    if (_fallbackDeviceClaims.TryGetValue(deviceKey, out var existingOwner) && existingOwner == estudianteId)
                    {
                        ((ICollection<KeyValuePair<string, Guid>>)_fallbackDeviceClaims).Remove(new KeyValuePair<string, Guid>(deviceKey, estudianteId));
                    }
                }

                _fallbackCounters.AddOrUpdate(sesionId, 0, (_, c) => Math.Max(0, c - 1));
                if (_fallbackCache.TryGetValue(sesionId, out var entry))
                {
                    entry.State.ContadorAsistentes = Math.Max(0, entry.State.ContadorAsistentes - 1);
                }
            }
        }
    }

    public async Task ResetAttendanceClaimsAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        // 1. Limpiar memoria local de respaldo
        var syncRoot = _fallbackSessionLocks.GetOrAdd(sesionId, _ => new object());
        lock (syncRoot)
        {
            var prefix = $"{sesionId:N}:";
            var keysToRemove = _fallbackAttendanceClaims.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in keysToRemove)
            {
                _fallbackAttendanceClaims.TryRemove(k, out _);
            }

            var devKeysToRemove = _fallbackDeviceClaims.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in devKeysToRemove)
            {
                _fallbackDeviceClaims.TryRemove(k, out _);
            }

            _fallbackCounters[sesionId] = 0;
            if (_fallbackCache.TryGetValue(sesionId, out var entry))
            {
                entry.State.ContadorAsistentes = 0;
            }
        }

        // 2. Limpiar en Redis
        var db = _connectionProvider.GetDatabase();
        if (db != null)
        {
            try
            {
                var counterKey = $"slac:sesion:{sesionId}:contador";
                await db.KeyDeleteAsync(counterKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al reiniciar contador de asistencia en Redis para sesión {SesionId}", sesionId);
            }
        }
    }
}
