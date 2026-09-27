using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Security;
using SLAC.Core.Security.Models;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using SLAC.Features.Attendance.Background;
using SLAC.Features.Attendance.Services;

namespace SLAC.Tests;

public class AttendanceSchedulerTests
{
    private readonly Guid _testInstitucionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task AperturaSesionJob_ExecutesSuccessfully_OpensSessionAndSetsRedisState()
    {
        // Arrange
        var listaId = Guid.NewGuid();
        var materiaId = Guid.NewGuid();
        var institucionId = _testInstitucionId;

        var mockListaRepo = new FakeListaAsistenciaRepository();
        mockListaRepo.Almacenar(new ListaAsistencia
        {
            Id = listaId,
            InstitucionId = institucionId,
            MateriaId = materiaId,
            Estado = "Programada"
        });

        var mockTokenService = new FakeTokenService();
        var mockSessionCache = new FakeSessionCacheRepository();
        var mockEventBus = new FakeSessionEventBus();
        var mockNotification = new FakeClassroomNotificationService();

        var job = new AperturaSesionJob(
            mockListaRepo,
            mockTokenService,
            mockSessionCache,
            mockEventBus,
            mockNotification,
            NullLogger<AperturaSesionJob>.Instance);

        var context = CreateJobExecutionContext(listaId, materiaId, institucionId);

        // Act
        await job.Execute(context);

        // Assert
        var lista = await mockListaRepo.ObtenerPorIdAsync(listaId);
        Assert.NotNull(lista);
        Assert.Equal("Abierta", lista.Estado);

        var activeSession = await mockSessionCache.GetActiveSessionAsync(listaId);
        Assert.NotNull(activeSession);
        Assert.Equal(listaId, activeSession.SesionId);
        Assert.Equal(0, activeSession.RotacionIndex);
        Assert.True(activeSession.EstaAbierta);

        Assert.Single(mockEventBus.PublishedEvents);
        Assert.Equal("sesion_iniciada", mockEventBus.PublishedEvents[0].TipoEvento);

        Assert.Single(mockNotification.RotatedCalls);
        Assert.Equal(0, mockNotification.RotatedCalls[0].rotacionIndex);
    }

    [Fact]
    public async Task CierreSesionJob_ExecutesSuccessfully_ClosesSessionAndComputesFaltasIdempotent()
    {
        // Arrange
        var listaId = Guid.NewGuid();
        var materiaId = Guid.NewGuid();
        var institucionId = _testInstitucionId;

        var estudiantePresenteId = Guid.NewGuid();
        var estudianteAusente1Id = Guid.NewGuid();
        var estudianteAusente2Id = Guid.NewGuid();

        var mockListaRepo = new FakeListaAsistenciaRepository();
        mockListaRepo.Almacenar(new ListaAsistencia
        {
            Id = listaId,
            InstitucionId = institucionId,
            MateriaId = materiaId,
            Estado = "Abierta"
        });

        var mockDetalleRepo = new FakeAsistenciaDetalleRepository();
        // Un estudiante ya registró asistencia (Presente)
        mockDetalleRepo.Almacenar(new AsistenciaDetalle
        {
            Id = Guid.NewGuid(),
            ListaId = listaId,
            InstitucionId = institucionId,
            EstudianteId = estudiantePresenteId,
            Estado = "Presente",
            Origen = "Qr"
        });

        var mockSuscripcionRepo = new FakeSuscripcionRepository();
        mockSuscripcionRepo.ConfigurarSuscripciones(materiaId, [estudiantePresenteId, estudianteAusente1Id, estudianteAusente2Id]);

        var mockSessionCache = new FakeSessionCacheRepository();
        await mockSessionCache.SetActiveSessionAsync(new SessionEphemeralState
        {
            SesionId = listaId,
            InstitucionId = institucionId,
            MateriaId = materiaId,
            EstaAbierta = true
        }, TimeSpan.FromMinutes(20));

        var mockEventBus = new FakeSessionEventBus();
        var mockNotification = new FakeClassroomNotificationService();

        var job = new CierreSesionJob(
            mockListaRepo,
            mockDetalleRepo,
            mockSuscripcionRepo,
            mockSessionCache,
            mockEventBus,
            mockNotification,
            NullLogger<CierreSesionJob>.Instance);

        var context = CreateJobExecutionContext(listaId, materiaId, institucionId);

        // Act
        await job.Execute(context);

        // Assert
        var lista = await mockListaRepo.ObtenerPorIdAsync(listaId);
        Assert.NotNull(lista);
        Assert.Equal("Cerrada", lista.Estado);
        Assert.NotNull(lista.HoraCierre);
        Assert.Equal(3, lista.TotalSuscritos);
        Assert.Equal(1, lista.TotalPresentes);
        Assert.Equal(2, lista.TotalFaltas);

        // Verificamos que se registraron exactamente 2 faltas para los ausentes
        var detalles = await mockDetalleRepo.ListarPorListaAsync(listaId);
        Assert.Equal(3, detalles.Count);
        Assert.Single(detalles, d => d.EstudianteId == estudiantePresenteId && d.Estado == "Presente");
        Assert.Single(detalles, d => d.EstudianteId == estudianteAusente1Id && d.Estado == "Falta" && d.Origen == "Cierre");
        Assert.Single(detalles, d => d.EstudianteId == estudianteAusente2Id && d.Estado == "Falta" && d.Origen == "Cierre");

        // Redis cache invalidado
        var activeSession = await mockSessionCache.GetActiveSessionAsync(listaId);
        Assert.Null(activeSession);

        // SignalR y Pub/Sub notificados
        Assert.Single(mockEventBus.PublishedEvents);
        Assert.Equal("sesion_cerrada", mockEventBus.PublishedEvents[0].TipoEvento);
        Assert.Single(mockNotification.ClosedCalls);
        Assert.Equal(1, mockNotification.ClosedCalls[0].totalPresentes);
        Assert.Equal(2, mockNotification.ClosedCalls[0].totalFaltas);
    }

    private static FakeJobExecutionContext CreateJobExecutionContext(Guid listaId, Guid materiaId, Guid institucionId)
    {
        var dataMap = new JobDataMap
        {
            { "ListaId", listaId.ToString() },
            { "MateriaId", materiaId.ToString() },
            { "InstitucionId", institucionId.ToString() }
        };

        var detail = JobBuilder.Create<AperturaSesionJob>()
            .UsingJobData(dataMap)
            .Build();

        var trigger = TriggerBuilder.Create()
            .UsingJobData(dataMap)
            .Build();

        return new FakeJobExecutionContext(detail, trigger, dataMap);
    }

    #region Fakes

    private sealed class FakeListaAsistenciaRepository : IListaAsistenciaRepository
    {
        private readonly List<ListaAsistencia> _items = [];

        public void Almacenar(ListaAsistencia item) => _items.Add(item);

        public Task<ListaAsistencia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));

        public Task<ListaAsistencia?> ObtenerPorMateriaYFechaAsync(Guid materiaId, DateOnly fecha, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.MateriaId == materiaId && x.Fecha == fecha));

        public Task<IReadOnlyList<ListaAsistencia>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ListaAsistencia>>([.. _items.Where(x => x.MateriaId == materiaId)]);

        public Task<ListaAsistencia> CrearOActualizarAsync(ListaAsistencia lista, CancellationToken ct = default)
        {
            var existing = _items.FirstOrDefault(x => x.Id == lista.Id);
            if (existing != null) _items.Remove(existing);
            _items.Add(lista);
            return Task.FromResult(lista);
        }

        public Task ActualizarEstadoAsync(Guid id, string nuevoEstado, TimeSpan? horaCierre = null, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.Estado = nuevoEstado;
                if (horaCierre.HasValue) item.HoraCierre = horaCierre;
            }
            return Task.CompletedTask;
        }

        public Task ActualizarTotalesAsync(Guid id, int totalSuscritos, int totalPresentes, int totalFaltas, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.TotalSuscritos = totalSuscritos;
                item.TotalPresentes = totalPresentes;
                item.TotalFaltas = totalFaltas;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAsistenciaDetalleRepository : IAsistenciaDetalleRepository
    {
        private readonly List<AsistenciaDetalle> _items = [];

        public void Almacenar(AsistenciaDetalle item) => _items.Add(item);

        public Task<IReadOnlyList<AsistenciaDetalle>> ListarPorListaAsync(Guid listaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AsistenciaDetalle>>([.. _items.Where(x => x.ListaId == listaId)]);

        public Task<AsistenciaDetalle?> ObtenerPorListaYEstudianteAsync(Guid listaId, Guid estudianteId, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.ListaId == listaId && x.EstudianteId == estudianteId));

        public Task<AsistenciaDetalle> RegistrarAsistenciaAsync(AsistenciaDetalle detalle, CancellationToken ct = default)
        {
            _items.Add(detalle);
            return Task.FromResult(detalle);
        }

        public Task<int> RegistrarFaltasIdempotenteAsync(Guid listaId, Guid institucionId, IEnumerable<Guid> estudiantesIds, CancellationToken ct = default)
        {
            var insertados = 0;
            foreach (var estId in estudiantesIds)
            {
                if (!_items.Any(x => x.ListaId == listaId && x.EstudianteId == estId))
                {
                    _items.Add(new AsistenciaDetalle
                    {
                        Id = Guid.NewGuid(),
                        ListaId = listaId,
                        InstitucionId = institucionId,
                        EstudianteId = estId,
                        Estado = "Falta",
                        Origen = "Cierre",
                        MinutosDesdeInicio = 0
                    });
                    insertados++;
                }
            }
            return Task.FromResult(insertados);
        }
    }

    private sealed class FakeSuscripcionRepository : ISuscripcionRepository
    {
        private readonly Dictionary<Guid, List<Guid>> _suscripcionesPorMateria = [];

        public void ConfigurarSuscripciones(Guid materiaId, IEnumerable<Guid> estudiantes)
            => _suscripcionesPorMateria[materiaId] = [.. estudiantes];

        public Task<IReadOnlyList<Guid>> ListarEstudiantesIdsPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
        {
            if (_suscripcionesPorMateria.TryGetValue(materiaId, out var list))
            {
                return Task.FromResult<IReadOnlyList<Guid>>([.. list]);
            }
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }

        public Task<IReadOnlyList<Suscripcion>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Suscripcion>>([]);

        public Task<Suscripcion?> ObtenerAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default)
            => Task.FromResult<Suscripcion?>(null);

        public Task<Suscripcion> SuscribirAsync(Suscripcion suscripcion, CancellationToken ct = default)
            => Task.FromResult(suscripcion);
    }

    private sealed class FakeTokenService : ITokenService
    {
        public string GenerateSessionQrToken(Guid sesionId, Guid institucionId, DateTimeOffset inicioVigencia, int ventanaMinutos, int rotacionIndex)
            => $"token_mock_{sesionId}_{rotacionIndex}";

        public bool TryValidateSessionQrToken(string tokenString, out SessionQrToken? sessionToken, out string? errorMessage, int rotacionTolerancia = 1)
        {
            sessionToken = null;
            errorMessage = null;
            return true;
        }

        public (string TokenString, string JtiHash) GenerateDeviceCredential(Guid dispositivoId, Guid institucionId)
            => ($"token_{dispositivoId}", $"hash_{dispositivoId}");

        public bool TryValidateDeviceCredential(string tokenString, out DeviceCredentialToken? deviceToken, out string? errorMessage)
        {
            deviceToken = null;
            errorMessage = null;
            return true;
        }
    }

    private sealed class FakeSessionCacheRepository : ISessionCacheRepository
    {
        private readonly Dictionary<Guid, SessionEphemeralState> _store = [];

        public Task SetActiveSessionAsync(SessionEphemeralState state, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _store[state.SesionId] = state;
            return Task.CompletedTask;
        }

        public Task<SessionEphemeralState?> GetActiveSessionAsync(Guid sesionId, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(sesionId, out var state);
            return Task.FromResult(state);
        }

        public Task<long> IncrementAttendanceCounterAsync(Guid sesionId, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task<bool> IsSessionActiveAsync(Guid sesionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.ContainsKey(sesionId));

        public Task InvalidateSessionAsync(Guid sesionId, CancellationToken cancellationToken = default)
        {
            _store.Remove(sesionId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionEventBus : ISessionEventBus
    {
        public List<AttendanceEventMessage> PublishedEvents { get; } = [];

        public Task PublishEventAsync(AttendanceEventMessage message, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(message);
            return Task.CompletedTask;
        }

        public Task SubscribeToSessionEventsAsync(Guid sesionId, Func<AttendanceEventMessage, Task> handler, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UnsubscribeFromSessionEventsAsync(Guid sesionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeClassroomNotificationService : IClassroomNotificationService
    {
        public List<(Guid sesionId, int totalPresentes, string? estudianteCodigo)> RecordedCalls { get; } = [];
        public List<(Guid sesionId, string nuevoToken, int rotacionIndex)> RotatedCalls { get; } = [];
        public List<(Guid sesionId, int totalPresentes, int totalFaltas)> ClosedCalls { get; } = [];

        public Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string? estudianteCodigo, CancellationToken cancellationToken = default)
        {
            RecordedCalls.Add((sesionId, totalPresentes, estudianteCodigo));
            return Task.CompletedTask;
        }

        public Task NotifyQrRotatedAsync(Guid sesionId, string nuevoToken, int rotacionIndex, CancellationToken cancellationToken = default)
        {
            RotatedCalls.Add((sesionId, nuevoToken, rotacionIndex));
            return Task.CompletedTask;
        }

        public Task NotifySessionClosedAsync(Guid sesionId, int totalPresentes, int totalFaltas, CancellationToken cancellationToken = default)
        {
            ClosedCalls.Add((sesionId, totalPresentes, totalFaltas));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJobExecutionContext(IJobDetail jobDetail, ITrigger trigger, JobDataMap mergedMap) : IJobExecutionContext
    {
        public IScheduler Scheduler => throw new NotImplementedException();
        public ITrigger Trigger { get; } = trigger;
        public ICalendar? Calendar => null;
        public bool Recovering => false;
        public TriggerKey TriggerKey => Trigger.Key;
        public TriggerKey RecoveringTriggerKey => Trigger.Key;
        public int RefireCount => 0;
        public JobDataMap MergedJobDataMap { get; } = mergedMap;
        public IJobDetail JobDetail { get; } = jobDetail;
        public IJob JobInstance => throw new NotImplementedException();
        public DateTimeOffset FireTimeUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc => DateTimeOffset.UtcNow;
        public DateTimeOffset? PreviousFireTimeUtc => null;
        public DateTimeOffset? NextFireTimeUtc => null;
        public string FireInstanceId => "fake-instance";
        public object? Result { get; set; }
        public TimeSpan JobRunTime => TimeSpan.Zero;
        public CancellationToken CancellationToken => CancellationToken.None;

        public void Put(object key, object objectValue) { }
        public object? Get(object key) => null;
    }

    #endregion
}
