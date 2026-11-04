using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;

namespace SLAC.Tests;

/// <summary>
/// Doble de pruebas en memoria concurrente de ISessionCacheRepository para pruebas unitarias y de estrés
/// con paridad de comportamiento total respecto a Redis y sus scripts Lua (Milestone 1).
/// </summary>
public class FakeSessionCacheRepository : ISessionCacheRepository
{
    private readonly ConcurrentDictionary<Guid, (SessionEphemeralState State, DateTimeOffset ExpiresAt)> _sessions = new();
    private readonly ConcurrentDictionary<Guid, long> _counters = new();
    private readonly ConcurrentDictionary<string, byte> _studentClaims = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Guid> _deviceClaims = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, (string LeaseId, DateTimeOffset ExpiresAt)> _projectionLocks = new();
    private readonly ConcurrentDictionary<Guid, object> _sessionLocks = new();

    private static string? NormalizeDeviceHash(string? deviceHash) =>
        string.IsNullOrWhiteSpace(deviceHash) ? null : deviceHash.Trim().ToLowerInvariant();

    public Task SetActiveSessionAsync(SessionEphemeralState state, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        _sessions[state.SesionId] = (state, DateTimeOffset.UtcNow.Add(ttl));
        _counters.TryAdd(state.SesionId, state.ContadorAsistentes);
        return Task.CompletedTask;
    }

    public Task<SessionEphemeralState?> GetActiveSessionAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sesionId, out var entry))
        {
            if (DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                return Task.FromResult<SessionEphemeralState?>(entry.State);
            }
            _sessions.TryRemove(sesionId, out _);
        }
        return Task.FromResult<SessionEphemeralState?>(null);
    }

    public Task<long> IncrementAttendanceCounterAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        var count = _counters.AddOrUpdate(sesionId, 1, (_, c) => c + 1);
        if (_sessions.TryGetValue(sesionId, out var entry))
        {
            entry.State.ContadorAsistentes = (int)count;
        }
        return Task.FromResult(count);
    }

    public Task<bool> IsSessionActiveAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sesionId, out var entry))
        {
            return Task.FromResult(DateTimeOffset.UtcNow <= entry.ExpiresAt);
        }
        return Task.FromResult(false);
    }

    public Task InvalidateSessionAsync(Guid sesionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sesionId, out _);
        _counters.TryRemove(sesionId, out _);
        _projectionLocks.TryRemove(sesionId, out _);
        _sessionLocks.TryRemove(sesionId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> TryAcquireProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, CancellationToken cancellationToken = default)
        => TryAcquireProjectionLockAsync(sesionId, leaseId, ttl, force: false, cancellationToken);

    public Task<bool> TryAcquireProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, bool force, CancellationToken cancellationToken = default)
    {
        if (sesionId == Guid.Empty) throw new ArgumentException("ID de sesión inválido", nameof(sesionId));
        if (string.IsNullOrWhiteSpace(leaseId)) throw new ArgumentException("LeaseId inválido", nameof(leaseId));
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl), "TTL debe ser positivo");

        var now = DateTimeOffset.UtcNow;
        var newExpiry = now.Add(ttl);

        while (true)
        {
            if (_projectionLocks.TryGetValue(sesionId, out var existing))
            {
                if (!force && existing.ExpiresAt > now && !string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
                {
                    return Task.FromResult(false);
                }

                if (_projectionLocks.TryUpdate(sesionId, (leaseId, newExpiry), existing))
                {
                    return Task.FromResult(true);
                }
            }
            else
            {
                if (_projectionLocks.TryAdd(sesionId, (leaseId, newExpiry)))
                {
                    return Task.FromResult(true);
                }
            }
        }
    }

    public Task<bool> RefreshProjectionLockAsync(Guid sesionId, string leaseId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (sesionId == Guid.Empty) throw new ArgumentException("ID de sesión inválido", nameof(sesionId));
        if (string.IsNullOrWhiteSpace(leaseId)) throw new ArgumentException("LeaseId inválido", nameof(leaseId));
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl), "TTL debe ser positivo");

        var now = DateTimeOffset.UtcNow;
        if (_projectionLocks.TryGetValue(sesionId, out var existing))
        {
            if (existing.ExpiresAt > now && string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
            {
                var newExpiry = now.Add(ttl);
                var updated = _projectionLocks.TryUpdate(sesionId, (leaseId, newExpiry), existing);
                return Task.FromResult(updated);
            }
        }
        return Task.FromResult(false);
    }

    public Task ReleaseProjectionLockAsync(Guid sesionId, string leaseId, CancellationToken cancellationToken = default)
    {
        if (_projectionLocks.TryGetValue(sesionId, out var existing))
        {
            if (string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
            {
                _projectionLocks.TryRemove(sesionId, out _);
            }
        }
        return Task.CompletedTask;
    }

    public Task<AttendanceClaimResult> TryClaimAttendanceAsync(
        Guid sesionId,
        Guid estudianteId,
        string? deviceHash,
        string? presenceCodeSubmitted,
        bool validatePresenceCode = true,
        CancellationToken cancellationToken = default)
    {
        if (sesionId == Guid.Empty) throw new ArgumentException("ID de sesión inválido", nameof(sesionId));
        if (estudianteId == Guid.Empty) throw new ArgumentException("ID de estudiante inválido", nameof(estudianteId));

        var syncRoot = _sessionLocks.GetOrAdd(sesionId, _ => new object());
        lock (syncRoot)
        {
            if (!_sessions.TryGetValue(sesionId, out var entry) || DateTimeOffset.UtcNow > entry.ExpiresAt)
            {
                return Task.FromResult(AttendanceClaimResult.Rejected(AttendanceClaimStatus.SessionInactive, "La sesión no existe o está inactiva."));
            }

            if (validatePresenceCode && !string.IsNullOrWhiteSpace(entry.State.CodigoPresenciaActual))
            {
                if (!string.Equals(entry.State.CodigoPresenciaActual.Trim(), presenceCodeSubmitted?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(AttendanceClaimResult.Rejected(AttendanceClaimStatus.InvalidPresenceCode, "Código de presencia visual incorrecto o expirado."));
                }
            }

            var studentKey = $"{sesionId:N}:{estudianteId:N}";
            if (_studentClaims.ContainsKey(studentKey))
            {
                var curCount = _counters.GetOrAdd(sesionId, entry.State.ContadorAsistentes);
                return Task.FromResult(AttendanceClaimResult.AlreadyDone(curCount));
            }

            var cleanDevice = NormalizeDeviceHash(deviceHash);
            if (!string.IsNullOrWhiteSpace(cleanDevice))
            {
                var deviceKey = $"{sesionId:N}:{cleanDevice}";
                if (_deviceClaims.TryGetValue(deviceKey, out var existingOwner) && existingOwner != estudianteId)
                {
                    return Task.FromResult(AttendanceClaimResult.Rejected(AttendanceClaimStatus.DeviceConflict, "Este dispositivo ya registró asistencia para otro alumno en esta sesión (RN-05)."));
                }
            }

            _studentClaims[studentKey] = 1;

            if (!string.IsNullOrWhiteSpace(cleanDevice))
            {
                var deviceKey = $"{sesionId:N}:{cleanDevice}";
                _deviceClaims[deviceKey] = estudianteId;
            }

            long newCount = _counters.AddOrUpdate(sesionId, _ => (long)entry.State.ContadorAsistentes + 1, (_, c) => c + 1);
            entry.State.ContadorAsistentes = (int)newCount;

            return Task.FromResult(AttendanceClaimResult.Ok(newCount));
        }
    }

    public Task RollbackAttendanceClaimAsync(
        Guid sesionId,
        Guid estudianteId,
        string? deviceHash,
        CancellationToken cancellationToken = default)
    {
        var syncRoot = _sessionLocks.GetOrAdd(sesionId, _ => new object());
        lock (syncRoot)
        {
            var studentKey = $"{sesionId:N}:{estudianteId:N}";
            if (_studentClaims.TryRemove(studentKey, out _))
            {
                var cleanDevice = NormalizeDeviceHash(deviceHash);
                if (!string.IsNullOrWhiteSpace(cleanDevice))
                {
                    var deviceKey = $"{sesionId:N}:{cleanDevice}";
                    if (_deviceClaims.TryGetValue(deviceKey, out var existingOwner) && existingOwner == estudianteId)
                    {
                        ((ICollection<KeyValuePair<string, Guid>>)_deviceClaims).Remove(new KeyValuePair<string, Guid>(deviceKey, estudianteId));
                    }
                }

                _counters.AddOrUpdate(sesionId, 0, (_, c) => Math.Max(0, c - 1));
                if (_sessions.TryGetValue(sesionId, out var entry))
                {
                    entry.State.ContadorAsistentes = Math.Max(0, entry.State.ContadorAsistentes - 1);
                }
            }
        }
        return Task.CompletedTask;
    }

    // Auxiliares para aserciones de prueba
    public long GetAttendanceCount(Guid sesionId) => _counters.TryGetValue(sesionId, out var c) ? c : 0;
    public bool IsStudentClaimed(Guid sesionId, Guid estudianteId) => _studentClaims.ContainsKey($"{sesionId:N}:{estudianteId:N}");
    public Guid? GetDeviceOwner(Guid sesionId, string deviceHash)
    {
        var clean = NormalizeDeviceHash(deviceHash);
        if (clean != null && _deviceClaims.TryGetValue($"{sesionId:N}:{clean}", out var owner))
            return owner;
        return null;
    }
    public string? GetLockHolder(Guid sesionId) =>
        _projectionLocks.TryGetValue(sesionId, out var l) && l.ExpiresAt > DateTimeOffset.UtcNow ? l.LeaseId : null;

    public void Reset()
    {
        _sessions.Clear();
        _counters.Clear();
        _studentClaims.Clear();
        _deviceClaims.Clear();
        _projectionLocks.Clear();
        _sessionLocks.Clear();
    }
}
