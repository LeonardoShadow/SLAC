using Microsoft.Extensions.Logging.Abstractions;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Docentes.Entities;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Core.Security;
using SLAC.Core.Security.Models;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using SLAC.Features.Attendance.Hubs;
using SLAC.Features.Attendance.Services;

namespace SLAC.Tests;

public class StudentAttendanceFlowTests
{
    private readonly Guid _institucionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ProcesarEscaneo_InvalidOrTamperedQrToken_ReturnsError()
    {
        // Arrange
        var fakes = new AttendanceTestFakes(_institucionId);
        var service = fakes.CreateSuscripcionService();

        var request = new AttendanceScanRequest
        {
            SesionId = Guid.NewGuid(),
            QrToken = "invalid_token_xyz"
        };

        // Act
        var result = await service.ProcesarEscaneoAsync(request);

        // Assert
        Assert.False(result.Exito);
        Assert.Contains("expirado o no es válido", result.MensajeError);
    }

    [Fact]
    public async Task ProcesarEscaneo_NewStudentWithoutDeviceCookie_RequestsRegistration()
    {
        // Arrange
        var fakes = new AttendanceTestFakes(_institucionId);
        var sesionId = Guid.NewGuid();
        var materiaId = Guid.NewGuid();

        fakes.SetupActiveSession(sesionId, materiaId);
        var qrToken = fakes.GenerateValidQrToken(sesionId);
        var service = fakes.CreateSuscripcionService();

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            DeviceToken = null // Sin cookie previa
        };

        // Act
        var result = await service.ProcesarEscaneoAsync(request);

        // Assert
        Assert.False(result.Exito);
        Assert.True(result.RequiereRegistro);
        Assert.Equal("Estructuras de Datos", result.MateriaNombre);
    }

    [Fact]
    public async Task ProcesarEscaneo_NewStudentWithFormData_CreatesStudentDeviceSubscriptionAndAttendance()
    {
        // Arrange
        var fakes = new AttendanceTestFakes(_institucionId);
        var sesionId = Guid.NewGuid();
        var materiaId = Guid.NewGuid();

        fakes.SetupActiveSession(sesionId, materiaId);
        var qrToken = fakes.GenerateValidQrToken(sesionId);
        var service = fakes.CreateSuscripcionService();

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            DeviceToken = null,
            Codigo = "EST-9988",
            Nombres = "Laura",
            Apellidos = "Vargas",
            Correo = "lvargas@universidad.edu",
            AceptaTerminos = true,
            UserAgent = "Mozilla/5.0 (iPhone)"
        };

        // Act
        var result = await service.ProcesarEscaneoAsync(request);

        // Assert
        Assert.True(result.Exito);
        Assert.False(result.YaRegistradoHoy);
        Assert.Equal("Laura Vargas", result.EstudianteNombre);
        Assert.Equal("EST-9988", result.EstudianteCodigo);
        Assert.NotNull(result.NuevaDeviceCookie); // Debe inyectar cookie

        // Verificar persistencia de Estudiante
        var estudiante = await fakes.EstudianteRepo.ObtenerPorCodigoAsync(_institucionId, "EST-9988");
        Assert.NotNull(estudiante);

        // Verificar suscripción activa
        var suscripcion = await fakes.SuscripcionRepo.ObtenerAsync(materiaId, estudiante.Id);
        Assert.NotNull(suscripcion);

        // Verificar detalle de asistencia Presente
        var detalle = await fakes.DetalleRepo.ObtenerPorListaYEstudianteAsync(sesionId, estudiante.Id);
        Assert.NotNull(detalle);
        Assert.Equal("Presente", detalle.Estado);
        Assert.Equal("QR", detalle.Origen);

        // Verificar notificaciones y contador en Redis
        Assert.Single(fakes.EventBus.PublishedEvents);
        Assert.Equal("asistencia_registrada", fakes.EventBus.PublishedEvents[0].TipoEvento);
        Assert.Single(fakes.NotificationService.RecordedCalls);
    }

    [Fact]
    public async Task ProcesarEscaneo_NewStudentSubscribingLate_ComputesRetroactiveAbsences()
    {
        // Arrange
        var fakes = new AttendanceTestFakes(_institucionId);
        var sesionActualId = Guid.NewGuid();
        var materiaId = Guid.NewGuid();

        // Configurar 2 sesiones anteriores 'Cerradas'
        var sesionPasada1 = fakes.SetupClosedSession(materiaId, DateOnly.FromDateTime(DateTime.Today.AddDays(-7)));
        var sesionPasada2 = fakes.SetupClosedSession(materiaId, DateOnly.FromDateTime(DateTime.Today.AddDays(-2)));

        fakes.SetupActiveSession(sesionActualId, materiaId);
        var qrToken = fakes.GenerateValidQrToken(sesionActualId);
        var service = fakes.CreateSuscripcionService();

        var request = new AttendanceScanRequest
        {
            SesionId = sesionActualId,
            QrToken = qrToken,
            Codigo = "EST-7766",
            Nombres = "Marcos",
            Apellidos = "Quiroga",
            Correo = "mquiroga@universidad.edu",
            AceptaTerminos = true
        };

        // Act
        var result = await service.ProcesarEscaneoAsync(request);

        // Assert
        Assert.True(result.Exito);
        var estudianteId = result.EstudianteId!.Value;

        // Sesión actual marcada como Presente
        var detalleActual = await fakes.DetalleRepo.ObtenerPorListaYEstudianteAsync(sesionActualId, estudianteId);
        Assert.NotNull(detalleActual);
        Assert.Equal("Presente", detalleActual.Estado);

        // Sesiones pasadas deben tener falta retroactiva con origen 'Suscripción tardía' (Paso 30)
        var falta1 = await fakes.DetalleRepo.ObtenerPorListaYEstudianteAsync(sesionPasada1.Id, estudianteId);
        Assert.NotNull(falta1);
        Assert.Equal("Falta", falta1.Estado);
        Assert.Equal("Suscripción tardía", falta1.Origen);

        var falta2 = await fakes.DetalleRepo.ObtenerPorListaYEstudianteAsync(sesionPasada2.Id, estudianteId);
        Assert.NotNull(falta2);
        Assert.Equal("Falta", falta2.Estado);
        Assert.Equal("Suscripción tardía", falta2.Origen);
    }

    [Fact]
    public async Task ProcesarEscaneo_ReturningStudentWithValidDeviceCookie_RecordsAttendanceInstantly()
    {
        // Arrange
        var fakes = new AttendanceTestFakes(_institucionId);
        var sesionId = Guid.NewGuid();
        var materiaId = Guid.NewGuid();

        fakes.SetupActiveSession(sesionId, materiaId);
        var qrToken = fakes.GenerateValidQrToken(sesionId);

        // Registrar estudiante previo con dispositivo vinculado
        var estudiante = new Estudiante
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            Codigo = "EST-1122",
            Nombres = "Andrea",
            Apellidos = "Paz",
            Correo = "apaz@universidad.edu"
        };
        await fakes.EstudianteRepo.CrearOActualizarAsync(estudiante);

        var dispId = Guid.NewGuid();
        var (tokenString, jtiHash) = fakes.TokenService.GenerateDeviceCredential(dispId, _institucionId);
        await fakes.DispositivoRepo.RegistrarDispositivoAsync(new Dispositivo
        {
            Id = dispId,
            JtiHash = jtiHash,
            Kid = "slac-key-v1"
        }, estudiante.Id, _institucionId);

        var service = fakes.CreateSuscripcionService();

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            DeviceToken = tokenString // Envía cookie válida
        };

        // Act
        var result = await service.ProcesarEscaneoAsync(request);

        // Assert
        Assert.True(result.Exito);
        Assert.False(result.RequiereRegistro);
        Assert.Equal("Andrea Paz", result.EstudianteNombre);
        Assert.Equal("EST-1122", result.EstudianteCodigo);

        // Segundo intento en la misma sesión (idempotente)
        var segundoEscaneo = await service.ProcesarEscaneoAsync(request);
        Assert.True(segundoEscaneo.Exito);
        Assert.True(segundoEscaneo.YaRegistradoHoy);
    }

    [Fact]
    public void AttendanceStudentView_GeneratesValidHtmlViews()
    {
        // Act & Assert Success View
        var successResult = new AttendanceScanResult
        {
            Exito = true,
            EstudianteNombre = "Sofia Morales",
            EstudianteCodigo = "EST-4433",
            MateriaNombre = "Cálculo I",
            MateriaCodigo = "MAT-101",
            HoraLlegada = DateTimeOffset.UtcNow,
            MinutosDesdeInicio = 3
        };
        var successHtml = AttendanceStudentView.RenderSuccessView(successResult);
        Assert.Contains("Sofia Morales", successHtml);
        Assert.Contains("¡Asistencia a Clases Registrada!", successHtml);
        Assert.Contains("Cálculo I", successHtml);

        // Act & Assert Registration View
        var regHtml = AttendanceStudentView.RenderRegistrationView(Guid.NewGuid(), "mock_token", "Física II", "FIS-201", "Faltan datos");
        Assert.Contains("Física II", regHtml);
        Assert.Contains("Faltan datos", regHtml);
        Assert.Contains("Código o Matrícula Institucional", regHtml);

        // Act & Assert Error View
        var errorHtml = AttendanceStudentView.RenderErrorView("Código Expirado", "Por favor solicita un nuevo código.");
        Assert.Contains("Código Expirado", errorHtml);
        Assert.Contains("Por favor solicita un nuevo código.", errorHtml);
    }

    #region Test Fakes Helper

    private sealed class AttendanceTestFakes(Guid institucionId)
    {
        public Guid InstitucionId { get; } = institucionId;
        public FakeTokenService TokenService { get; } = new();
        public FakeListaAsistenciaRepository ListaRepo { get; } = new();
        public FakeAsistenciaDetalleRepository DetalleRepo { get; } = new();
        public FakeSuscripcionRepository SuscripcionRepo { get; } = new();
        public FakeEstudianteRepository EstudianteRepo { get; } = new();
        public FakeDispositivoRepository DispositivoRepo { get; } = new();
        public FakeRevinculacionRepository RevinculacionRepo { get; } = new();
        public FakeRevinculacionService RevinculacionService { get; } = new();
        public FakeMateriaRepository MateriaRepo { get; } = new();
        public FakeSessionCacheRepository SessionCache { get; } = new();
        public FakeSessionEventBus EventBus { get; } = new();
        public FakeClassroomNotificationService NotificationService { get; } = new();

        public SuscripcionService CreateSuscripcionService() => new(
            TokenService,
            ListaRepo,
            DetalleRepo,
            SuscripcionRepo,
            EstudianteRepo,
            DispositivoRepo,
            RevinculacionRepo,
            RevinculacionService,
            MateriaRepo,
            SessionCache,
            EventBus,
            NotificationService,
            NullLogger<SuscripcionService>.Instance);

        public void SetupActiveSession(Guid sesionId, Guid materiaId)
        {
            var materia = new Materia
            {
                Id = materiaId,
                InstitucionId = InstitucionId,
                Codigo = "INF-131",
                Nombre = "Estructuras de Datos",
                HoraInicio = new TimeSpan(8, 0, 0),
                Dias = "L, M, X"
            };
            MateriaRepo.Almacenar(materia);

            var lista = new ListaAsistencia
            {
                Id = sesionId,
                InstitucionId = InstitucionId,
                MateriaId = materiaId,
                Fecha = DateOnly.FromDateTime(DateTime.Today),
                HoraInicio = new TimeSpan(8, 0, 0),
                Estado = "Abierta"
            };
            ListaRepo.Almacenar(lista);

            SessionCache.SetActiveSessionAsync(new SessionEphemeralState
            {
                SesionId = sesionId,
                InstitucionId = InstitucionId,
                MateriaId = materiaId,
                EstaAbierta = true
            }, TimeSpan.FromMinutes(20)).GetAwaiter().GetResult();
        }

        public ListaAsistencia SetupClosedSession(Guid materiaId, DateOnly fecha)
        {
            var lista = new ListaAsistencia
            {
                Id = Guid.NewGuid(),
                InstitucionId = InstitucionId,
                MateriaId = materiaId,
                Fecha = fecha,
                HoraInicio = new TimeSpan(8, 0, 0),
                HoraCierre = new TimeSpan(8, 20, 0),
                Estado = "Cerrada",
                TotalSuscritos = 5,
                TotalPresentes = 4,
                TotalFaltas = 1
            };
            ListaRepo.Almacenar(lista);
            return lista;
        }

        public string GenerateValidQrToken(Guid sesionId)
            => TokenService.GenerateSessionQrToken(sesionId, InstitucionId, DateTimeOffset.UtcNow, 20, 0);
    }

    private sealed class FakeTokenService : ITokenService
    {
        private readonly Dictionary<string, SessionQrToken> _sessionTokens = [];
        private readonly Dictionary<string, DeviceCredentialToken> _deviceTokens = [];

        public string GenerateSessionQrToken(Guid sesionId, Guid institucionId, DateTimeOffset inicioVigencia, int ventanaMinutos, int rotacionIndex)
        {
            var tokenStr = $"session_token_{sesionId}_{rotacionIndex}";
            _sessionTokens[tokenStr] = new SessionQrToken
            {
                SesionId = sesionId,
                InstitucionId = institucionId,
                InicioVigenciaUnix = inicioVigencia.ToUnixTimeSeconds(),
                VencimientoUnix = inicioVigencia.AddMinutes(ventanaMinutos).ToUnixTimeSeconds(),
                RotacionIndex = rotacionIndex,
                Nonce = Guid.NewGuid().ToString("N"),
                Kid = "slac-key-v1"
            };
            return tokenStr;
        }

        public bool TryValidateSessionQrToken(string tokenString, out SessionQrToken? sessionToken, out string? errorMessage, int rotacionTolerancia = 1)
        {
            if (_sessionTokens.TryGetValue(tokenString, out var token))
            {
                sessionToken = token;
                errorMessage = null;
                return true;
            }
            sessionToken = null;
            errorMessage = "El código QR ha expirado o no es válido.";
            return false;
        }

        public (string TokenString, string JtiHash) GenerateDeviceCredential(Guid dispositivoId, Guid institucionId)
        {
            var tokenStr = $"device_token_{dispositivoId}";
            var jtiHash = $"hash_{dispositivoId}";
            _deviceTokens[tokenStr] = new DeviceCredentialToken
            {
                DispositivoId = dispositivoId,
                InstitucionId = institucionId,
                Jti = jtiHash,
                EmitidoEnUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Kid = "slac-key-v1"
            };
            return (tokenStr, jtiHash);
        }

        public bool TryValidateDeviceCredential(string tokenString, out DeviceCredentialToken? deviceToken, out string? errorMessage)
        {
            if (_deviceTokens.TryGetValue(tokenString, out var token))
            {
                deviceToken = token;
                errorMessage = null;
                return true;
            }
            deviceToken = null;
            errorMessage = "Credencial de dispositivo inválida.";
            return false;
        }
    }

    private sealed class FakeEstudianteRepository : IEstudianteRepository
    {
        private readonly List<Estudiante> _items = [];

        public Task<Estudiante?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));

        public Task<Estudiante?> ObtenerPorCodigoAsync(Guid institucionId, string codigo, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.InstitucionId == institucionId && x.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase)));

        public Task<Estudiante?> ObtenerPorCorreoAsync(Guid institucionId, string correo, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.InstitucionId == institucionId && x.Correo.Equals(correo, StringComparison.OrdinalIgnoreCase)));

        public Task<Estudiante> CrearOActualizarAsync(Estudiante estudiante, CancellationToken ct = default)
        {
            var existing = _items.FirstOrDefault(x => x.Id == estudiante.Id || (x.InstitucionId == estudiante.InstitucionId && x.Codigo.Equals(estudiante.Codigo, StringComparison.OrdinalIgnoreCase)));
            if (existing != null) _items.Remove(existing);
            _items.Add(estudiante);
            return Task.FromResult(estudiante);
        }

        public Task<IReadOnlyList<Estudiante>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Estudiante>>([.. _items.Where(x => x.InstitucionId == institucionId)]);
    }

    private sealed class FakeDispositivoRepository : IDispositivoRepository
    {
        private readonly List<Dispositivo> _dispositivos = [];
        private readonly List<DispositivoEstudiante> _vinculaciones = [];

        public Task<Dispositivo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_dispositivos.FirstOrDefault(x => x.Id == id));

        public Task<Dispositivo?> ObtenerPorJtiHashAsync(string jtiHash, CancellationToken ct = default)
            => Task.FromResult(_dispositivos.FirstOrDefault(x => x.JtiHash == jtiHash));

        public Task<Dispositivo> RegistrarDispositivoAsync(Dispositivo dispositivo, Guid estudianteId, Guid institucionId, CancellationToken ct = default)
        {
            _dispositivos.Add(dispositivo);
            _vinculaciones.Add(new DispositivoEstudiante
            {
                Id = Guid.NewGuid(),
                DispositivoId = dispositivo.Id,
                EstudianteId = estudianteId,
                InstitucionId = institucionId,
                Activo = true
            });
            return Task.FromResult(dispositivo);
        }

        public Task<Estudiante?> ObtenerEstudiantePorDispositivoAsync(Guid dispositivoId, Guid institucionId, CancellationToken ct = default)
        {
            var vinc = _vinculaciones.FirstOrDefault(x => x.DispositivoId == dispositivoId && x.InstitucionId == institucionId && x.Activo);
            if (vinc != null)
            {
                return Task.FromResult<Estudiante?>(new Estudiante
                {
                    Id = vinc.EstudianteId,
                    InstitucionId = institucionId,
                    Codigo = "EST-1122",
                    Nombres = "Andrea",
                    Apellidos = "Paz"
                });
            }
            return Task.FromResult<Estudiante?>(null);
        }

        public Task ActualizarUltimoUsoAsync(Guid dispositivoId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RevocarDispositivoAsync(Guid dispositivoId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<DispositivoEstudiante?> ObtenerVinculacionActivaAsync(Guid estudianteId, Guid institucionId, CancellationToken ct = default)
            => Task.FromResult(_vinculaciones.FirstOrDefault(x => x.EstudianteId == estudianteId && x.InstitucionId == institucionId && x.Activo));
    }

    private sealed class FakeMateriaRepository : IMateriaRepository
    {
        private readonly List<Materia> _items = [];

        public void Almacenar(Materia m) => _items.Add(m);

        public Task<Materia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<Materia>> ListarPorDocenteAsync(Guid docenteId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Materia>>([.. _items.Where(x => x.DocenteId == docenteId)]);

        public Task<IReadOnlyList<Materia>> ListarActivasPorDiaAsync(Guid institucionId, string diaSigla, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Materia>>([.. _items.Where(x => x.InstitucionId == institucionId)]);

        public Task<IReadOnlyList<Materia>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Materia>>([.. _items.Where(x => x.InstitucionId == institucionId)]);

        public Task<bool> ArchivarAsync(Guid id, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null) item.Estado = "archivada";
            return Task.FromResult(true);
        }

        public Task<Materia> GuardarAsync(Materia materia, CancellationToken ct = default)
        {
            _items.Add(materia);
            return Task.FromResult(materia);
        }
    }

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
            _items.Add(lista);
            return Task.FromResult(lista);
        }

        public Task ActualizarEstadoAsync(Guid id, string nuevoEstado, TimeSpan? horaCierre = null, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null) item.Estado = nuevoEstado;
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

        public Task<IReadOnlyList<AsistenciaDetalle>> ListarPorListaAsync(Guid listaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AsistenciaDetalle>>([.. _items.Where(x => x.ListaId == listaId)]);

        public Task<AsistenciaDetalle?> ObtenerPorListaYEstudianteAsync(Guid listaId, Guid estudianteId, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.ListaId == listaId && x.EstudianteId == estudianteId));

        public Task<AsistenciaDetalle> RegistrarAsistenciaAsync(AsistenciaDetalle detalle, CancellationToken ct = default)
        {
            var existing = _items.FirstOrDefault(x => x.ListaId == detalle.ListaId && x.EstudianteId == detalle.EstudianteId);
            if (existing != null) _items.Remove(existing);
            _items.Add(detalle);
            return Task.FromResult(detalle);
        }

        public Task<int> RegistrarFaltasIdempotenteAsync(Guid listaId, Guid institucionId, IEnumerable<Guid> estudiantesIds, CancellationToken ct = default)
        {
            var count = 0;
            foreach (var id in estudiantesIds)
            {
                if (!_items.Any(x => x.ListaId == listaId && x.EstudianteId == id))
                {
                    _items.Add(new AsistenciaDetalle
                    {
                        ListaId = listaId,
                        EstudianteId = id,
                        InstitucionId = institucionId,
                        Estado = "Falta",
                        Origen = "Cierre"
                    });
                    count++;
                }
            }
            return Task.FromResult(count);
        }
    }

    private sealed class FakeSuscripcionRepository : ISuscripcionRepository
    {
        private readonly List<Suscripcion> _items = [];

        public Task<IReadOnlyList<Suscripcion>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Suscripcion>>([.. _items.Where(x => x.MateriaId == materiaId)]);

        public Task<IReadOnlyList<Guid>> ListarEstudiantesIdsPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([.. _items.Where(x => x.MateriaId == materiaId).Select(x => x.EstudianteId)]);

        public Task<Suscripcion?> ObtenerAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId));

        public Task<Suscripcion> SuscribirAsync(Suscripcion suscripcion, CancellationToken ct = default)
        {
            if (!_items.Any(x => x.MateriaId == suscripcion.MateriaId && x.EstudianteId == suscripcion.EstudianteId))
            {
                _items.Add(suscripcion);
            }
            return Task.FromResult(suscripcion);
        }
    }

    private sealed class FakeSessionCacheRepository : ISessionCacheRepository
    {
        private readonly Dictionary<Guid, SessionEphemeralState> _store = [];
        private long _counter;

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
            => Task.FromResult(Interlocked.Increment(ref _counter));

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

        public Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string? estudianteCodigo, CancellationToken cancellationToken = default)
        {
            RecordedCalls.Add((sesionId, totalPresentes, estudianteCodigo));
            return Task.CompletedTask;
        }

        public Task NotifyQrRotatedAsync(Guid sesionId, string nuevoToken, int rotacionIndex, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifySessionClosedAsync(Guid sesionId, int totalPresentes, int totalFaltas, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeRevinculacionRepository : IRevinculacionRepository
    {
        private readonly List<Revinculacion> _items = [];

        public Task<Revinculacion> CrearSolicitudAsync(Revinculacion solicitud, CancellationToken ct = default)
        {
            _ = ct;
            _items.Add(solicitud);
            return Task.FromResult(solicitud);
        }

        public Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult<IReadOnlyList<Revinculacion>>([.. _items.Where(x => x.MateriaId == materiaId && x.EstaPendiente)]);
        }

        public Task<Revinculacion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
        }

        public Task<Revinculacion?> ObtenerPendientePorEstudianteYMateriaAsync(Guid estudianteId, Guid materiaId, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult(_items.FirstOrDefault(x => x.EstudianteId == estudianteId && x.MateriaId == materiaId && x.EstaPendiente));
        }

        public Task MarcarComoUsadaAsync(Guid id, CancellationToken ct = default)
        {
            _ = ct;
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null) item.UsadaEn = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRevinculacionService : IRevinculacionService
    {
        public Task<SolicitudRevinculacionResult> SolicitarRevinculacionAsync(Guid sesionId, string codigoEstudiante, string userAgent, CancellationToken ct = default)
        {
            _ = sesionId;
            _ = userAgent;
            _ = ct;
            return Task.FromResult(new SolicitudRevinculacionResult(true, false, Guid.NewGuid(), "Solicitud en espera.", "Juan", codigoEstudiante));
        }

        public Task<(bool Exito, string? Error)> AutorizarRevinculacionAsync(Guid solicitudId, string actorDocente, CancellationToken ct = default)
        {
            _ = solicitudId;
            _ = actorDocente;
            _ = ct;
            return Task.FromResult<(bool, string?)>((true, null));
        }

        public Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
        {
            _ = materiaId;
            _ = ct;
            return Task.FromResult<IReadOnlyList<Revinculacion>>([]);
        }
    }

    #endregion
}
