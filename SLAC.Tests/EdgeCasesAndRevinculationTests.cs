using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Docentes.Entities;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Core.Security;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using SLAC.Features.Attendance.Background;
using SLAC.Features.Attendance.Hubs;
using SLAC.Features.Attendance.Services;

namespace SLAC.Tests;

public class EdgeCasesAndRevinculationTests
{
    private readonly Guid _institucionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _materiaId = Guid.NewGuid();
    private readonly Guid _docenteId = Guid.NewGuid();

    [Fact]
    public async Task RevinculacionService_SolicitarRevinculacion_CreaSolicitudPendiente_CuandoEstudianteExiste()
    {
        // Arrange
        var revRepo = new MockRevinculacionRepo();
        var estRepo = new MockEstudianteRepo();
        var dispRepo = new MockDispositivoRepo();
        var listaRepo = new MockListaRepo();
        var auditRepo = new MockAuditoriaRepo();
        var hubContext = new MockHubContext();

        var estudiante = new Estudiante
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            Codigo = "EST-999",
            Nombres = "Valeria",
            Apellidos = "Rojas",
            Correo = "valeria@universidad.edu"
        };
        await estRepo.CrearOActualizarAsync(estudiante);

        var sesion = new ListaAsistencia
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            MateriaId = _materiaId,
            DocenteId = _docenteId,
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            HoraInicio = new TimeSpan(8, 0, 0),
            Estado = "Abierta"
        };
        await listaRepo.CrearOActualizarAsync(sesion);

        var service = new RevinculacionService(
            revRepo, estRepo, dispRepo, listaRepo, auditRepo, hubContext,
            NullLogger<RevinculacionService>.Instance);

        // Act
        var result = await service.SolicitarRevinculacionAsync(sesion.Id, "EST-999", "Mozilla/5.0 (iPhone)");

        // Assert
        Assert.True(result.SolicitudCreada);
        Assert.NotNull(result.SolicitudId);
        Assert.Equal("Valeria Rojas", result.EstudianteNombre);
        Assert.Equal("EST-999", result.EstudianteCodigo);

        var pendientes = await revRepo.ListarPendientesPorMateriaAsync(_materiaId);
        Assert.Single(pendientes);
        Assert.Equal(result.SolicitudId.Value, pendientes[0].Id);
        Assert.Equal("Mozilla/5.0 (iPhone)", pendientes[0].DispositivoAgente);
        Assert.Null(pendientes[0].UsadaEn);
    }

    [Fact]
    public async Task RevinculacionService_SolicitarRevinculacion_RetornaExistente_SiYaHaySolicitudPendiente()
    {
        // Arrange
        var revRepo = new MockRevinculacionRepo();
        var estRepo = new MockEstudianteRepo();
        var dispRepo = new MockDispositivoRepo();
        var listaRepo = new MockListaRepo();
        var auditRepo = new MockAuditoriaRepo();
        var hubContext = new MockHubContext();

        var estudiante = new Estudiante
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            Codigo = "EST-888",
            Nombres = "Mateo",
            Apellidos = "Castro",
            Correo = "mateo@universidad.edu"
        };
        await estRepo.CrearOActualizarAsync(estudiante);

        var sesion = new ListaAsistencia
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            MateriaId = _materiaId,
            DocenteId = _docenteId,
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            HoraInicio = new TimeSpan(9, 0, 0),
            Estado = "Abierta"
        };
        await listaRepo.CrearOActualizarAsync(sesion);

        var service = new RevinculacionService(
            revRepo, estRepo, dispRepo, listaRepo, auditRepo, hubContext,
            NullLogger<RevinculacionService>.Instance);

        // Act - Primera y Segunda solicitud
        var primera = await service.SolicitarRevinculacionAsync(sesion.Id, "EST-888", "Agent 1");
        var segunda = await service.SolicitarRevinculacionAsync(sesion.Id, "EST-888", "Agent 2");

        // Assert - No se duplican las solicitudes pendientes
        Assert.True(primera.SolicitudCreada);
        Assert.True(segunda.SolicitudCreada);
        Assert.Equal(primera.SolicitudId, segunda.SolicitudId);

        var pendientes = await revRepo.ListarPendientesPorMateriaAsync(_materiaId);
        Assert.Single(pendientes);
    }

    [Fact]
    public async Task RevinculacionService_AutorizarRevinculacion_RevocaDispositivoPrevio_YApruebaSolicitud_YRegistraAuditoria()
    {
        // Arrange
        var revRepo = new MockRevinculacionRepo();
        var estRepo = new MockEstudianteRepo();
        var dispRepo = new MockDispositivoRepo();
        var listaRepo = new MockListaRepo();
        var auditRepo = new MockAuditoriaRepo();
        var hubContext = new MockHubContext();

        var estudiante = new Estudiante
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            Codigo = "EST-777",
            Nombres = "Lucía",
            Apellidos = "Fernández",
            Correo = "lucia@universidad.edu"
        };
        await estRepo.CrearOActualizarAsync(estudiante);

        // Dispositivo anterior activo
        var dispAnterior = new Dispositivo
        {
            Id = Guid.NewGuid(),
            JtiHash = "hash-anterior",
            RevocadoEn = null
        };
        await dispRepo.RegistrarDispositivoAsync(dispAnterior, estudiante.Id, _institucionId);

        var solicitud = new Revinculacion
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            EstudianteId = estudiante.Id,
            MateriaId = _materiaId,
            DocenteId = _docenteId,
            EstudianteNombre = estudiante.NombreCompleto,
            EstudianteCodigo = estudiante.Codigo,
            ExpiraEn = DateTimeOffset.UtcNow.AddHours(1)
        };
        await revRepo.CrearSolicitudAsync(solicitud);

        var service = new RevinculacionService(
            revRepo, estRepo, dispRepo, listaRepo, auditRepo, hubContext,
            NullLogger<RevinculacionService>.Instance);

        // Act - Docente autoriza
        var (exito, error) = await service.AutorizarRevinculacionAsync(solicitud.Id, "Prof. Gómez");

        // Assert
        Assert.True(exito);
        Assert.Null(error);

        // 1. Solicitud marcada como usada
        var actualizada = await revRepo.ObtenerPorIdAsync(solicitud.Id);
        Assert.NotNull(actualizada?.UsadaEn);

        // 2. Dispositivo anterior revocado
        var dispRevocado = await dispRepo.ObtenerPorIdAsync(dispAnterior.Id);
        Assert.NotNull(dispRevocado);
        Assert.True(dispRevocado.EstaRevocado);

        // 3. Auditoría inmutable registrada
        var auditorias = await auditRepo.ListarPorInstitucionAsync(_institucionId);
        Assert.Contains(auditorias, a => a.Evento == "revinculacion_dispositivo" && a.Actor == "Prof. Gómez");
    }

    [Fact]
    public async Task RevinculacionService_AutorizarRevinculacion_FallaSiYaFueUsada()
    {
        // Arrange
        var revRepo = new MockRevinculacionRepo();
        var estRepo = new MockEstudianteRepo();
        var dispRepo = new MockDispositivoRepo();
        var listaRepo = new MockListaRepo();
        var auditRepo = new MockAuditoriaRepo();
        var hubContext = new MockHubContext();

        var solicitudId = Guid.NewGuid();
        var solicitud = new Revinculacion
        {
            Id = solicitudId,
            InstitucionId = _institucionId,
            EstudianteId = Guid.NewGuid(),
            MateriaId = _materiaId,
            DocenteId = _docenteId,
            ExpiraEn = DateTimeOffset.UtcNow.AddHours(1),
            UsadaEn = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        await revRepo.CrearSolicitudAsync(solicitud);

        var service = new RevinculacionService(
            revRepo, estRepo, dispRepo, listaRepo, auditRepo, hubContext,
            NullLogger<RevinculacionService>.Instance);

        // Act
        var (exito, error) = await service.AutorizarRevinculacionAsync(solicitudId, "Prof. Gómez");

        // Assert
        Assert.False(exito);
        Assert.Contains("ya fue autorizada", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuspenderClaseEmergencia_MarcaSesionSuspendida_InvalidaCache_YRegistraAuditoria()
    {
        // Arrange
        var listaRepo = new MockListaRepo();
        var detalleRepo = new MockDetalleRepo();
        var auditRepo = new MockAuditoriaRepo();
        var sessionCache = new MockSessionCache();
        var hubContext = new MockHubContext();

        var sesion = new ListaAsistencia
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            MateriaId = _materiaId,
            DocenteId = _docenteId,
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            HoraInicio = new TimeSpan(14, 0, 0),
            Estado = "Abierta",
            TotalPresentes = 5,
            TotalFaltas = 0
        };
        await listaRepo.CrearOActualizarAsync(sesion);

        var service = new CorreccionAsistenciaService(
            listaRepo, detalleRepo, auditRepo, sessionCache, hubContext,
            NullLogger<CorreccionAsistenciaService>.Instance);

        // Act
        var (exito, error) = await service.SuspenderClaseEmergenciaAsync(
            sesion.Id,
            "Corte imprevisto de suministro eléctrico en el pabellón de aulas.",
            "Ing. Pérez");

        // Assert
        Assert.True(exito);
        Assert.Null(error);

        // 1. Estado actualizado a 'Suspendida'
        var sesionActualizada = await listaRepo.ObtenerPorIdAsync(sesion.Id);
        Assert.NotNull(sesionActualizada);
        Assert.Equal("Suspendida", sesionActualizada.Estado);

        // 2. Caché invalidado en Redis
        Assert.True(sessionCache.FueInvalidada(sesion.Id));

        // 3. Auditoría inmutable registrada con el motivo
        var auditorias = await auditRepo.ListarPorInstitucionAsync(_institucionId);
        var auditEvento = auditorias.FirstOrDefault(a => a.Evento == "suspension_clase");
        Assert.NotNull(auditEvento);
        Assert.Equal("Ing. Pérez", auditEvento.Actor);
        Assert.Contains("suministro", auditEvento.DatosJson);
    }

    [Fact]
    public async Task SuspenderClaseEmergencia_RechazaMotivoInvalidoOCorto()
    {
        // Arrange
        var listaRepo = new MockListaRepo();
        var detalleRepo = new MockDetalleRepo();
        var auditRepo = new MockAuditoriaRepo();
        var sessionCache = new MockSessionCache();
        var hubContext = new MockHubContext();

        var sesionId = Guid.NewGuid();
        var service = new CorreccionAsistenciaService(
            listaRepo, detalleRepo, auditRepo, sessionCache, hubContext,
            NullLogger<CorreccionAsistenciaService>.Instance);

        // Act & Assert
        var (exito1, err1) = await service.SuspenderClaseEmergenciaAsync(sesionId, "", "Docente");
        Assert.False(exito1);
        Assert.Contains("mínimo 5 caracteres", err1);

        var (exito2, err2) = await service.SuspenderClaseEmergenciaAsync(sesionId, "  no ", "Docente");
        Assert.False(exito2);
        Assert.Contains("mínimo 5 caracteres", err2);
    }

    [Fact]
    public async Task CierreSesionJob_NoGeneraInasistencias_SiSesionEstaSuspendida()
    {
        // Arrange
        var listaRepo = new MockListaRepo();
        var detalleRepo = new MockDetalleRepo();
        var suscripcionRepo = new MockSuscripcionRepo();
        var sessionCache = new MockSessionCache();
        var eventBus = new MockSessionEventBus();
        var notifService = new MockNotificationService();

        var sesion = new ListaAsistencia
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            MateriaId = _materiaId,
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            HoraInicio = new TimeSpan(10, 0, 0),
            Estado = "Suspendida" // Marcada como Suspendida por emergencia
        };
        await listaRepo.CrearOActualizarAsync(sesion);

        // Estudiantes suscritos a la materia que normalmente recibirían 'Falta'
        suscripcionRepo.ConfigurarEstudiantes(_materiaId, [Guid.NewGuid(), Guid.NewGuid()]);

        var job = new CierreSesionJob(
            listaRepo, detalleRepo, suscripcionRepo, sessionCache, eventBus, notifService,
            NullLogger<CierreSesionJob>.Instance);

        var context = new MockJobExecutionContext(new JobDataMap
        {
            { "ListaId", sesion.Id.ToString() },
            { "MateriaId", _materiaId.ToString() },
            { "InstitucionId", _institucionId.ToString() }
        });

        // Act
        await job.Execute(context);

        // Assert - La sesión suspendida NO debe generar faltas automáticas
        var detalles = await detalleRepo.ListarPorListaAsync(sesion.Id);
        Assert.Empty(detalles);

        // Estado sigue siendo 'Suspendida', no sobreescrita a 'Cerrada'
        var sesionVerificada = await listaRepo.ObtenerPorIdAsync(sesion.Id);
        Assert.Equal("Suspendida", sesionVerificada?.Estado);
    }

    [Fact]
    public async Task CorregirAsistencia_FaltaAPresente_ActualizaDetalle_AjustaContadores_YRegistraAuditoria()
    {
        // Arrange
        var listaRepo = new MockListaRepo();
        var detalleRepo = new MockDetalleRepo();
        var auditRepo = new MockAuditoriaRepo();
        var sessionCache = new MockSessionCache();
        var hubContext = new MockHubContext();

        var sesion = new ListaAsistencia
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            MateriaId = _materiaId,
            DocenteId = _docenteId,
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            HoraInicio = new TimeSpan(7, 0, 0),
            Estado = "Cerrada",
            TotalPresentes = 10,
            TotalFaltas = 2
        };
        await listaRepo.CrearOActualizarAsync(sesion);

        var estudianteId = Guid.NewGuid();
        var detalle = new AsistenciaDetalle
        {
            Id = Guid.NewGuid(),
            ListaId = sesion.Id,
            EstudianteId = estudianteId,
            Estado = "Falta",
            Origen = "Ausente"
        };
        await detalleRepo.RegistrarAsistenciaAsync(detalle);

        var service = new CorreccionAsistenciaService(
            listaRepo, detalleRepo, auditRepo, sessionCache, hubContext,
            NullLogger<CorreccionAsistenciaService>.Instance);

        // Act - Docente justifica la falta
        var (exito, error) = await service.CorregirAsistenciaAsync(
            sesion.Id,
            estudianteId,
            "Presente",
            "Presentó certificado médico oficial avalado por Dirección de Carrera.",
            "Dr. Morales");

        // Assert
        Assert.True(exito);
        Assert.Null(error);

        // 1. Detalle actualizado
        var detalleActualizado = await detalleRepo.ObtenerPorListaYEstudianteAsync(sesion.Id, estudianteId);
        Assert.NotNull(detalleActualizado);
        Assert.Equal("Presente", detalleActualizado.Estado);
        Assert.Equal("Corrección", detalleActualizado.Origen);

        // 2. Contadores actualizados
        var sesionActualizada = await listaRepo.ObtenerPorIdAsync(sesion.Id);
        Assert.NotNull(sesionActualizada);
        Assert.Equal(11, sesionActualizada.TotalPresentes);
        Assert.Equal(1, sesionActualizada.TotalFaltas);

        // 3. Auditoría inmutable registrada con estado anterior y nuevo
        var auditorias = await auditRepo.ListarPorInstitucionAsync(_institucionId);
        var auditEvento = auditorias.FirstOrDefault(a => a.Evento == "correccion_asistencia");
        Assert.NotNull(auditEvento);
        Assert.Equal("Dr. Morales", auditEvento.Actor);
        Assert.Contains("\"estadoAnterior\":\"Falta\"", auditEvento.DatosJson);
        Assert.Contains("\"nuevoEstado\":\"Presente\"", auditEvento.DatosJson);
        Assert.Contains("certificado", auditEvento.DatosJson);
    }

    [Fact]
    public async Task CorregirAsistencia_PresenteAFalta_AjustaContadoresInversos()
    {
        // Arrange
        var listaRepo = new MockListaRepo();
        var detalleRepo = new MockDetalleRepo();
        var auditRepo = new MockAuditoriaRepo();
        var sessionCache = new MockSessionCache();
        var hubContext = new MockHubContext();

        var sesion = new ListaAsistencia
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            MateriaId = _materiaId,
            DocenteId = _docenteId,
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            HoraInicio = new TimeSpan(7, 0, 0),
            Estado = "Cerrada",
            TotalPresentes = 15,
            TotalFaltas = 3
        };
        await listaRepo.CrearOActualizarAsync(sesion);

        var estudianteId = Guid.NewGuid();
        var detalle = new AsistenciaDetalle
        {
            Id = Guid.NewGuid(),
            ListaId = sesion.Id,
            EstudianteId = estudianteId,
            Estado = "Presente",
            Origen = "QR"
        };
        await detalleRepo.RegistrarAsistenciaAsync(detalle);

        var service = new CorreccionAsistenciaService(
            listaRepo, detalleRepo, auditRepo, sessionCache, hubContext,
            NullLogger<CorreccionAsistenciaService>.Instance);

        // Act - Docente anula presencia por abandono de clase
        var (exito, error) = await service.CorregirAsistenciaAsync(
            sesion.Id,
            estudianteId,
            "Falta",
            "Estudiante se retiró sin autorización previa antes de la evaluación.",
            "Lic. Suárez");

        // Assert
        Assert.True(exito);
        Assert.Null(error);

        var detalleActualizado = await detalleRepo.ObtenerPorListaYEstudianteAsync(sesion.Id, estudianteId);
        Assert.NotNull(detalleActualizado);
        Assert.Equal("Falta", detalleActualizado.Estado);
        Assert.Equal("Corrección", detalleActualizado.Origen);

        var sesionActualizada = await listaRepo.ObtenerPorIdAsync(sesion.Id);
        Assert.NotNull(sesionActualizada);
        Assert.Equal(14, sesionActualizada.TotalPresentes);
        Assert.Equal(4, sesionActualizada.TotalFaltas);
    }

    [Fact]
    public async Task CorregirAsistencia_RechazaMotivoInvalido()
    {
        // Arrange
        var listaRepo = new MockListaRepo();
        var detalleRepo = new MockDetalleRepo();
        var auditRepo = new MockAuditoriaRepo();
        var sessionCache = new MockSessionCache();
        var hubContext = new MockHubContext();

        var service = new CorreccionAsistenciaService(
            listaRepo, detalleRepo, auditRepo, sessionCache, hubContext,
            NullLogger<CorreccionAsistenciaService>.Instance);

        // Act & Assert
        var (exito, err) = await service.CorregirAsistenciaAsync(
            Guid.NewGuid(), Guid.NewGuid(), "Presente", " abc ", "Docente");

        Assert.False(exito);
        Assert.Contains("mínimo 5 caracteres", err);
    }

    [Fact]
    public async Task SuscripcionService_DetectaDispositivoPrevioActivo_YExigeRevinculacion()
    {
        // Arrange
        var tokenService = new TokenService(new KeyManager());
        var sessionCache = new MockSessionCache();
        var listaRepo = new MockListaRepo();
        var materiaRepo = new MockMateriaRepo();
        var estudianteRepo = new MockEstudianteRepo();
        var dispositivoRepo = new MockDispositivoRepo();
        var suscripcionRepo = new MockSuscripcionRepo();
        var revinculacionRepo = new MockRevinculacionRepo();
        var auditoriaRepo = new MockAuditoriaRepo();
        var hubContext = new MockHubContext();
        var eventBus = new MockSessionEventBus();
        var notificationService = new MockNotificationService();
        var detalleRepo = new MockDetalleRepo();

        var revService = new RevinculacionService(
            revinculacionRepo, estudianteRepo, dispositivoRepo, listaRepo, auditoriaRepo, hubContext,
            NullLogger<RevinculacionService>.Instance);

        var suscripcionService = new SuscripcionService(
            tokenService, listaRepo, detalleRepo, suscripcionRepo, estudianteRepo,
            dispositivoRepo, revinculacionRepo, revService, materiaRepo, sessionCache, eventBus,
            notificationService, NullLogger<SuscripcionService>.Instance);

        var materia = new Materia
        {
            Id = _materiaId,
            InstitucionId = _institucionId,
            Nombre = "Estructuras de Datos",
            Codigo = "SIS-201"
        };
        await materiaRepo.GuardarAsync(materia);

        var sesion = new ListaAsistencia
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            MateriaId = _materiaId,
            DocenteId = _docenteId,
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            HoraInicio = new TimeSpan(8, 0, 0),
            Estado = "Abierta"
        };
        await listaRepo.CrearOActualizarAsync(sesion);

        // Activar la sesión en caché
        await sessionCache.SetActiveSessionAsync(new SessionEphemeralState
        {
            SesionId = sesion.Id,
            InstitucionId = _institucionId,
            InicioVigencia = DateTimeOffset.UtcNow,
            Vencimiento = DateTimeOffset.UtcNow.AddMinutes(20),
            MateriaId = _materiaId,
            DocenteId = _docenteId
        }, TimeSpan.FromMinutes(20));

        // Estudiante ya registrado en la BD con un dispositivo vinculado
        var estudiante = new Estudiante
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            Codigo = "SIS-1001",
            Nombres = "Rodrigo",
            Apellidos = "Mendoza",
            Correo = "rodrigo@universidad.edu"
        };
        await estudianteRepo.CrearOActualizarAsync(estudiante);

        var dispPrevio = new Dispositivo
        {
            Id = Guid.NewGuid(),
            JtiHash = "hash-disp-previo",
            RevocadoEn = null
        };
        await dispositivoRepo.RegistrarDispositivoAsync(dispPrevio, estudiante.Id, _institucionId);

        // Token QR válido para la sesión
        var qrToken = tokenService.GenerateSessionQrToken(sesion.Id, _institucionId, DateTimeOffset.UtcNow, 20, rotacionIndex: 0);

        // Estudiante escanea desde un NUEVO dispositivo (sin cookie de dispositivo previa) y envía el formulario
        var request = new AttendanceScanRequest
        {
            SesionId = sesion.Id,
            QrToken = qrToken,
            DeviceToken = null,
            Codigo = "SIS-1001",
            Nombres = "Rodrigo",
            Apellidos = "Mendoza",
            Correo = "rodrigo@universidad.edu",
            AceptaTerminos = true,
            UserAgent = "Mozilla/5.0 (Android 14; Mobile)"
        };

        // Act
        var scanResult = await suscripcionService.ProcesarEscaneoAsync(request);

        // Assert - RN-05: Se detecta que ya tiene dispositivo activo y se exige autorización de revinculación
        Assert.False(scanResult.Exito);
        Assert.True(scanResult.RequiereRevinculacion);
        Assert.NotNull(scanResult.MensajeRevinculacion);
        Assert.Contains("autoriz", scanResult.MensajeRevinculacion, StringComparison.OrdinalIgnoreCase);

        // Se creó la solicitud pendiente
        var pendientes = await revinculacionRepo.ListarPendientesPorMateriaAsync(_materiaId);
        Assert.Single(pendientes);
        Assert.Equal(estudiante.Id, pendientes[0].EstudianteId);
    }

    // ==========================================
    // Mocks dedicados para las pruebas de Fase 10
    // ==========================================

    private class MockAuditoriaRepo : IAuditoriaRepository
    {
        private readonly List<Auditoria> _eventos = [];

        public Task RegistrarEventoAsync(Auditoria auditoria, CancellationToken ct = default)
        {
            _ = ct;
            if (auditoria.Id == Guid.Empty) auditoria.Id = Guid.NewGuid();
            _eventos.Add(auditoria);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Auditoria>> ListarPorInstitucionAsync(Guid institucionId, int limite = 50, CancellationToken ct = default)
        {
            _ = limite;
            _ = ct;
            return Task.FromResult<IReadOnlyList<Auditoria>>([.. _eventos.Where(a => a.InstitucionId == institucionId)]);
        }

        public Task<IReadOnlyList<Auditoria>> ListarPorEntidadAsync(string entidad, Guid institucionId, int limite = 50, CancellationToken ct = default)
        {
            _ = limite;
            _ = ct;
            return Task.FromResult<IReadOnlyList<Auditoria>>([.. _eventos.Where(a => a.InstitucionId == institucionId && a.Entidad == entidad)]);
        }
    }

    private class MockRevinculacionRepo : IRevinculacionRepository
    {
        private readonly List<Revinculacion> _solicitudes = [];

        public Task<Revinculacion> CrearSolicitudAsync(Revinculacion solicitud, CancellationToken ct = default)
        {
            _ = ct;
            if (solicitud.Id == Guid.Empty) solicitud.Id = Guid.NewGuid();
            _solicitudes.Add(solicitud);
            return Task.FromResult(solicitud);
        }

        public Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult<IReadOnlyList<Revinculacion>>([.. _solicitudes.Where(s => s.MateriaId == materiaId && !s.UsadaEn.HasValue && s.ExpiraEn > DateTimeOffset.UtcNow)]);
        }

        public Task<Revinculacion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult(_solicitudes.FirstOrDefault(s => s.Id == id));
        }

        public Task<Revinculacion?> ObtenerPendientePorEstudianteYMateriaAsync(Guid estudianteId, Guid materiaId, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult(_solicitudes.FirstOrDefault(s => s.EstudianteId == estudianteId && s.MateriaId == materiaId && !s.UsadaEn.HasValue && s.ExpiraEn > DateTimeOffset.UtcNow));
        }

        public Task MarcarComoUsadaAsync(Guid id, CancellationToken ct = default)
        {
            _ = ct;
            var sol = _solicitudes.FirstOrDefault(s => s.Id == id);
            if (sol != null)
            {
                sol.UsadaEn = DateTimeOffset.UtcNow;
            }
            return Task.CompletedTask;
        }
    }

    private class MockDispositivoRepo : IDispositivoRepository
    {
        private readonly List<Dispositivo> _dispositivos = [];
        private readonly List<DispositivoEstudiante> _vinculaciones = [];
        private readonly List<Estudiante> _estudiantes = [];

        public Task<Dispositivo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_dispositivos.FirstOrDefault(d => d.Id == id));

        public Task<Dispositivo?> ObtenerPorJtiHashAsync(string jtiHash, CancellationToken ct = default) =>
            Task.FromResult(_dispositivos.FirstOrDefault(d => d.JtiHash == jtiHash));

        public Task<Dispositivo> RegistrarDispositivoAsync(Dispositivo dispositivo, Guid estudianteId, Guid institucionId, CancellationToken ct = default)
        {
            if (dispositivo.Id == Guid.Empty) dispositivo.Id = Guid.NewGuid();
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
            var vinc = _vinculaciones.FirstOrDefault(v => v.DispositivoId == dispositivoId && v.InstitucionId == institucionId && v.Activo);
            if (vinc == null) return Task.FromResult<Estudiante?>(null);
            return Task.FromResult(_estudiantes.FirstOrDefault(e => e.Id == vinc.EstudianteId));
        }

        public Task ActualizarUltimoUsoAsync(Guid dispositivoId, CancellationToken ct = default)
        {
            var disp = _dispositivos.FirstOrDefault(d => d.Id == dispositivoId);
            if (disp != null) disp.UltimoUsoEn = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task RevocarDispositivoAsync(Guid dispositivoId, CancellationToken ct = default)
        {
            var disp = _dispositivos.FirstOrDefault(d => d.Id == dispositivoId);
            if (disp != null)
            {
                disp.RevocadoEn = DateTimeOffset.UtcNow;
            }
            var vincs = _vinculaciones.Where(v => v.DispositivoId == dispositivoId);
            foreach (var v in vincs) v.Activo = false;
            return Task.CompletedTask;
        }

        public Task<DispositivoEstudiante?> ObtenerVinculacionActivaAsync(Guid estudianteId, Guid institucionId, CancellationToken ct = default) =>
            Task.FromResult(_vinculaciones.FirstOrDefault(v => v.EstudianteId == estudianteId && v.InstitucionId == institucionId && v.Activo));
    }

    private class MockSessionCache : ISessionCacheRepository
    {
        private readonly Dictionary<Guid, SessionEphemeralState> _sessions = [];
        private readonly HashSet<Guid> _invalidated = [];

        public Task SetActiveSessionAsync(SessionEphemeralState state, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _sessions[state.SesionId] = state;
            return Task.CompletedTask;
        }

        public Task<SessionEphemeralState?> GetActiveSessionAsync(Guid sesionId, CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue(sesionId, out var state);
            return Task.FromResult(state);
        }

        public Task<long> IncrementAttendanceCounterAsync(Guid sesionId, CancellationToken cancellationToken = default) => Task.FromResult(1L);

        public Task<bool> IsSessionActiveAsync(Guid sesionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sessions.ContainsKey(sesionId) && !_invalidated.Contains(sesionId));

        public Task InvalidateSessionAsync(Guid sesionId, CancellationToken cancellationToken = default)
        {
            _invalidated.Add(sesionId);
            _sessions.Remove(sesionId);
            return Task.CompletedTask;
        }

        public bool FueInvalidada(Guid sesionId) => _invalidated.Contains(sesionId);
    }

    private class MockListaRepo : IListaAsistenciaRepository
    {
        private readonly List<ListaAsistencia> _listas = [];

        public Task<ListaAsistencia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_listas.FirstOrDefault(l => l.Id == id));

        public Task<ListaAsistencia?> ObtenerPorMateriaYFechaAsync(Guid materiaId, DateOnly fecha, CancellationToken ct = default) =>
            Task.FromResult(_listas.FirstOrDefault(l => l.MateriaId == materiaId && l.Fecha == fecha));

        public Task<IReadOnlyList<ListaAsistencia>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ListaAsistencia>>([.. _listas.Where(l => l.MateriaId == materiaId)]);

        public Task<ListaAsistencia> CrearOActualizarAsync(ListaAsistencia lista, CancellationToken ct = default)
        {
            if (lista.Id == Guid.Empty) lista.Id = Guid.NewGuid();
            _listas.RemoveAll(l => l.Id == lista.Id);
            _listas.Add(lista);
            return Task.FromResult(lista);
        }

        public Task ActualizarEstadoAsync(Guid id, string nuevoEstado, TimeSpan? horaCierre = null, CancellationToken ct = default)
        {
            var l = _listas.FirstOrDefault(x => x.Id == id);
            if (l != null)
            {
                l.Estado = nuevoEstado;
                l.HoraCierre = horaCierre;
            }
            return Task.CompletedTask;
        }

        public Task ActualizarTotalesAsync(Guid id, int totalSuscritos, int totalPresentes, int totalFaltas, CancellationToken ct = default)
        {
            var l = _listas.FirstOrDefault(x => x.Id == id);
            if (l != null)
            {
                l.TotalSuscritos = totalSuscritos;
                l.TotalPresentes = totalPresentes;
                l.TotalFaltas = totalFaltas;
            }
            return Task.CompletedTask;
        }
    }

    private class MockDetalleRepo : IAsistenciaDetalleRepository
    {
        private readonly List<AsistenciaDetalle> _detalles = [];

        public Task<IReadOnlyList<AsistenciaDetalle>> ListarPorListaAsync(Guid listaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AsistenciaDetalle>>([.. _detalles.Where(d => d.ListaId == listaId)]);

        public Task<AsistenciaDetalle?> ObtenerPorListaYEstudianteAsync(Guid listaId, Guid estudianteId, CancellationToken ct = default) =>
            Task.FromResult(_detalles.FirstOrDefault(d => d.ListaId == listaId && d.EstudianteId == estudianteId));

        public Task<AsistenciaDetalle> RegistrarAsistenciaAsync(AsistenciaDetalle detalle, CancellationToken ct = default)
        {
            if (detalle.Id == Guid.Empty) detalle.Id = Guid.NewGuid();
            _detalles.RemoveAll(d => d.ListaId == detalle.ListaId && d.EstudianteId == detalle.EstudianteId);
            _detalles.Add(detalle);
            return Task.FromResult(detalle);
        }

        public Task EliminarPorListaAsync(Guid listaId, CancellationToken ct = default)
        {
            _detalles.RemoveAll(d => d.ListaId == listaId);
            return Task.CompletedTask;
        }

        public Task<int> RegistrarFaltasIdempotenteAsync(Guid listaId, Guid institucionId, IEnumerable<Guid> estudiantesIds, CancellationToken ct = default)
        {
            int added = 0;
            foreach (var estId in estudiantesIds)
            {
                if (!_detalles.Any(d => d.ListaId == listaId && d.EstudianteId == estId))
                {
                    _detalles.Add(new AsistenciaDetalle
                    {
                        Id = Guid.NewGuid(),
                        ListaId = listaId,
                        EstudianteId = estId,
                        Estado = "Falta",
                        Origen = "Ausente"
                    });
                    added++;
                }
            }
            return Task.FromResult(added);
        }
    }

    private class MockEstudianteRepo : IEstudianteRepository
    {
        private readonly List<Estudiante> _estudiantes = [];

        public Task<Estudiante?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_estudiantes.FirstOrDefault(e => e.Id == id));

        public Task<Estudiante?> ObtenerPorCodigoAsync(Guid institucionId, string codigo, CancellationToken ct = default) =>
            Task.FromResult(_estudiantes.FirstOrDefault(e => e.InstitucionId == institucionId && e.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase)));

        public Task<Estudiante?> ObtenerPorCorreoAsync(Guid institucionId, string correo, CancellationToken ct = default) =>
            Task.FromResult(_estudiantes.FirstOrDefault(e => e.InstitucionId == institucionId && e.Correo.Equals(correo, StringComparison.OrdinalIgnoreCase)));

        public Task<Estudiante> CrearOActualizarAsync(Estudiante estudiante, CancellationToken ct = default)
        {
            if (estudiante.Id == Guid.Empty) estudiante.Id = Guid.NewGuid();
            _estudiantes.RemoveAll(e => e.Id == estudiante.Id);
            _estudiantes.Add(estudiante);
            return Task.FromResult(estudiante);
        }

        public Task<IReadOnlyList<Estudiante>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Estudiante>>([.. _estudiantes.Where(e => e.InstitucionId == institucionId)]);

        public Task<bool> EliminarAsync(Guid id, CancellationToken ct = default)
        {
            var count = _estudiantes.RemoveAll(e => e.Id == id);
            return Task.FromResult(count > 0);
        }
    }

    private class MockMateriaRepo : IMateriaRepository
    {
        private readonly List<Materia> _materias = [];

        public Task<IReadOnlyList<Materia>> ListarPorDocenteAsync(Guid docenteId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Materia>>([.. _materias.Where(m => m.DocenteId == docenteId)]);

        public Task<IReadOnlyList<Materia>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Materia>>([.. _materias.Where(m => m.InstitucionId == institucionId)]);

        public Task<IReadOnlyList<Materia>> ListarActivasPorDiaAsync(Guid institucionId, string diaSigla, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Materia>>([.. _materias.Where(m => m.InstitucionId == institucionId && m.Estado == "activa" && m.ObtenerListaDias().Contains(diaSigla))]);

        public Task<Materia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_materias.FirstOrDefault(m => m.Id == id));

        public Task<Materia> GuardarAsync(Materia materia, CancellationToken ct = default)
        {
            if (materia.Id == Guid.Empty) materia.Id = Guid.NewGuid();
            _materias.RemoveAll(m => m.Id == materia.Id);
            _materias.Add(materia);
            return Task.FromResult(materia);
        }

        public Task<bool> ArchivarAsync(Guid id, CancellationToken ct = default)
        {
            _ = id;
            _ = ct;
            return Task.FromResult(true);
        }
    }

    private class MockSuscripcionRepo : ISuscripcionRepository
    {
        private readonly Dictionary<Guid, List<Guid>> _estudiantesPorMateria = [];

        public void ConfigurarEstudiantes(Guid materiaId, IEnumerable<Guid> estudiantes)
        {
            _estudiantesPorMateria[materiaId] = [.. estudiantes];
        }

        public Task<bool> ExisteSuscripcionAsync(Guid estudianteId, Guid materiaId, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult(_estudiantesPorMateria.TryGetValue(materiaId, out var list) && list.Contains(estudianteId));
        }

        public Task RegistrarSuscripcionAsync(Guid estudianteId, Guid materiaId, CancellationToken ct = default)
        {
            _ = ct;
            if (!_estudiantesPorMateria.TryGetValue(materiaId, out var list))
            {
                list = [];
                _estudiantesPorMateria[materiaId] = list;
            }
            if (!list.Contains(estudianteId)) list.Add(estudianteId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> ListarEstudiantesIdsPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult<IReadOnlyList<Guid>>(_estudiantesPorMateria.TryGetValue(materiaId, out var list) ? [.. list] : []);
        }

        public Task<IReadOnlyList<Suscripcion>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult<IReadOnlyList<Suscripcion>>([]);
        }

        public Task<Suscripcion?> ObtenerAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default)
        {
            _ = materiaId;
            _ = estudianteId;
            _ = ct;
            return Task.FromResult<Suscripcion?>(null);
        }

        public Task<Suscripcion> SuscribirAsync(Suscripcion suscripcion, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult(suscripcion);
        }

        public Task<bool> DesinscribirAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default)
        {
            _ = ct;
            if (_estudiantesPorMateria.TryGetValue(materiaId, out var list))
            {
                list.Remove(estudianteId);
            }
            return Task.FromResult(true);
        }
    }

    private class MockSessionEventBus : ISessionEventBus
    {
        public Task PublishEventAsync(AttendanceEventMessage message, CancellationToken cancellationToken = default)
        {
            _ = message;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
        public Task SubscribeToSessionEventsAsync(Guid sesionId, Func<AttendanceEventMessage, Task> handler, CancellationToken cancellationToken = default)
        {
            _ = sesionId;
            _ = handler;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
        public Task UnsubscribeFromSessionEventsAsync(Guid sesionId, CancellationToken cancellationToken = default)
        {
            _ = sesionId;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private class MockNotificationService : IClassroomNotificationService
    {
        public Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string? estudianteCodigo, CancellationToken cancellationToken = default)
        {
            _ = sesionId;
            _ = totalPresentes;
            _ = estudianteCodigo;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string estudianteNombre, string? estudianteCodigo, CancellationToken cancellationToken = default)
        {
            _ = sesionId;
            _ = totalPresentes;
            _ = estudianteNombre;
            _ = estudianteCodigo;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
        public Task NotifyQrRotatedAsync(Guid sesionId, string nuevoToken, int rotacionIndex, CancellationToken cancellationToken = default)
        {
            _ = sesionId;
            _ = nuevoToken;
            _ = rotacionIndex;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
        public Task NotifySessionClosedAsync(Guid sesionId, int totalPresentes, int totalFaltas, CancellationToken cancellationToken = default)
        {
            _ = sesionId;
            _ = totalPresentes;
            _ = totalFaltas;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private class MockJobExecutionContext(JobDataMap dataMap) : IJobExecutionContext
    {
        private static readonly TriggerKey s_defaultTriggerKey = new("key", "group");

        public JobDataMap MergedJobDataMap { get; } = dataMap;
        public CancellationToken CancellationToken => CancellationToken.None;

        public IScheduler Scheduler => throw new NotImplementedException();
        public ITrigger Trigger => throw new NotImplementedException();
        public ICalendar? Calendar => throw new NotImplementedException();
        public bool Recovering => throw new NotImplementedException();
        public static TriggerKey TriggerKey { get; } = s_defaultTriggerKey;
        public TriggerKey RecoveringTriggerKey => s_defaultTriggerKey;
        public int RefireCount => throw new NotImplementedException();
        public IJobDetail JobDetail => throw new NotImplementedException();
        public IJob JobInstance => throw new NotImplementedException();
        public DateTimeOffset FireTimeUtc => DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc => throw new NotImplementedException();
        public DateTimeOffset? PreviousFireTimeUtc => throw new NotImplementedException();
        public DateTimeOffset? NextFireTimeUtc => throw new NotImplementedException();
        public string FireInstanceId => "fake-instance";
        public object? Result { get; set; }
        public TimeSpan JobRunTime => throw new NotImplementedException();
        public void Put(object key, object objectValue) { }
        public object? Get(object key) => null;
    }

    private class MockHubContext : IHubContext<AttendanceHub>
    {
        public IHubClients Clients { get; } = new MockHubClients();
        public IGroupManager Groups { get; } = new MockGroupManager();
    }

    private class MockHubClients : IHubClients
    {
        public IClientProxy All => new MockClientProxy();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new MockClientProxy();
        public IClientProxy Client(string connectionId) => new MockClientProxy();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new MockClientProxy();
        public IClientProxy Group(string groupName) => new MockClientProxy();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new MockClientProxy();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new MockClientProxy();
        public IClientProxy User(string userId) => new MockClientProxy();
        public IClientProxy Users(IReadOnlyList<string> userIds) => new MockClientProxy();
    }

    private class MockClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class MockGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
