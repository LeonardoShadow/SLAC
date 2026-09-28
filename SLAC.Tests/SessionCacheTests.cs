using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SLAC.Core.Configuration;
using SLAC.Core.Session.Models;
using SLAC.Infrastructure.Redis;
using Xunit;

namespace SLAC.Tests;

public class SessionCacheTests
{
    private readonly RedisConnectionProvider _connectionProvider;
    private readonly RedisSessionCacheRepository _cacheRepository;
    private readonly RedisSessionEventBus _eventBus;

    public SessionCacheTests()
    {
        var options = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost:6379,abortConnect=false"
        });

        _connectionProvider = new RedisConnectionProvider(options, NullLogger<RedisConnectionProvider>.Instance);
        _cacheRepository = new RedisSessionCacheRepository(_connectionProvider, NullLogger<RedisSessionCacheRepository>.Instance);
        _eventBus = new RedisSessionEventBus(_connectionProvider, NullLogger<RedisSessionEventBus>.Instance);
    }

    [Fact]
    public async Task SetActiveSession_And_GetActiveSession_ShouldStoreAndRetrieveState()
    {
        // Arrange
        var sesionId = Guid.NewGuid();
        var state = new SessionEphemeralState
        {
            SesionId = sesionId,
            InstitucionId = Guid.NewGuid(),
            MateriaId = Guid.NewGuid(),
            DocenteId = Guid.NewGuid(),
            InicioVigencia = DateTimeOffset.UtcNow,
            Vencimiento = DateTimeOffset.UtcNow.AddMinutes(20),
            RotacionIndex = 0,
            TokenActual = "sample-token-jwt",
            ContadorAsistentes = 0,
            EstaAbierta = true
        };

        // Act
        await _cacheRepository.SetActiveSessionAsync(state, TimeSpan.FromMinutes(20));
        var retrieved = await _cacheRepository.GetActiveSessionAsync(sesionId);
        var isActive = await _cacheRepository.IsSessionActiveAsync(sesionId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(sesionId, retrieved.SesionId);
        Assert.True(isActive);
        Assert.Equal("sample-token-jwt", retrieved.TokenActual);
    }

    [Fact]
    public async Task IncrementAttendanceCounter_ShouldIncreaseAtomically()
    {
        // Arrange
        var sesionId = Guid.NewGuid();
        var state = new SessionEphemeralState
        {
            SesionId = sesionId,
            InstitucionId = Guid.NewGuid(),
            MateriaId = Guid.NewGuid(),
            DocenteId = Guid.NewGuid(),
            InicioVigencia = DateTimeOffset.UtcNow,
            Vencimiento = DateTimeOffset.UtcNow.AddMinutes(20),
            ContadorAsistentes = 0
        };

        await _cacheRepository.SetActiveSessionAsync(state, TimeSpan.FromMinutes(20));

        // Act
        var count1 = await _cacheRepository.IncrementAttendanceCounterAsync(sesionId);
        var count2 = await _cacheRepository.IncrementAttendanceCounterAsync(sesionId);

        // Assert
        Assert.True(count1 >= 1);
        Assert.True(count2 > count1);
    }

    [Fact]
    public async Task InvalidateSession_ShouldRemoveSessionFromCache()
    {
        // Arrange
        var sesionId = Guid.NewGuid();
        var state = new SessionEphemeralState
        {
            SesionId = sesionId,
            InstitucionId = Guid.NewGuid(),
            MateriaId = Guid.NewGuid(),
            DocenteId = Guid.NewGuid(),
            InicioVigencia = DateTimeOffset.UtcNow,
            Vencimiento = DateTimeOffset.UtcNow.AddMinutes(20)
        };

        await _cacheRepository.SetActiveSessionAsync(state, TimeSpan.FromMinutes(20));
        Assert.True(await _cacheRepository.IsSessionActiveAsync(sesionId));

        // Act
        await _cacheRepository.InvalidateSessionAsync(sesionId);

        // Assert
        Assert.False(await _cacheRepository.IsSessionActiveAsync(sesionId));
        Assert.Null(await _cacheRepository.GetActiveSessionAsync(sesionId));
    }

    [Fact]
    public async Task EventBus_PublishAndSubscribe_ShouldReceiveAttendanceEvents()
    {
        // Arrange
        var sesionId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<AttendanceEventMessage>();

        await _eventBus.SubscribeToSessionEventsAsync(sesionId, msg =>
        {
            tcs.TrySetResult(msg);
            return Task.CompletedTask;
        });

        var eventToSend = new AttendanceEventMessage
        {
            TipoEvento = "asistencia_registrada",
            SesionId = sesionId,
            TotalPresentes = 42,
            EstudianteCodigo = "EST-2026-99"
        };

        // Act
        await _eventBus.PublishEventAsync(eventToSend);
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));

        // Assert
        Assert.Equal(tcs.Task, completedTask);
        var received = await tcs.Task;
        Assert.Equal(sesionId, received.SesionId);
        Assert.Equal(42, received.TotalPresentes);
        Assert.Equal("EST-2026-99", received.EstudianteCodigo);
    }
}
