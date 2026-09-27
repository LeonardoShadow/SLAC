using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Core.Security;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;

namespace SLAC.Features.Attendance.Services;

public class SuscripcionService(
    ITokenService tokenService,
    IListaAsistenciaRepository listaRepo,
    IAsistenciaDetalleRepository detalleRepo,
    ISuscripcionRepository suscripcionRepo,
    IEstudianteRepository estudianteRepo,
    IDispositivoRepository dispositivoRepo,
    IMateriaRepository materiaRepo,
    ISessionCacheRepository sessionCache,
    ISessionEventBus eventBus,
    IClassroomNotificationService notificationService,
    ILogger<SuscripcionService> logger) : ISuscripcionService
{
    private readonly ITokenService _tokenService = tokenService;
    private readonly IListaAsistenciaRepository _listaRepo = listaRepo;
    private readonly IAsistenciaDetalleRepository _detalleRepo = detalleRepo;
    private readonly ISuscripcionRepository _suscripcionRepo = suscripcionRepo;
    private readonly IEstudianteRepository _estudianteRepo = estudianteRepo;
    private readonly IDispositivoRepository _dispositivoRepo = dispositivoRepo;
    private readonly IMateriaRepository _materiaRepo = materiaRepo;
    private readonly ISessionCacheRepository _sessionCache = sessionCache;
    private readonly ISessionEventBus _eventBus = eventBus;
    private readonly IClassroomNotificationService _notificationService = notificationService;
    private readonly ILogger<SuscripcionService> _logger = logger;

    public async Task<AttendanceScanResult> ProcesarEscaneoAsync(AttendanceScanRequest request, CancellationToken ct = default)
    {
        // 1. Validar Token QR firmado (ES256, vigencia de 20 min y tolerancia de rotación de 15s)
        if (string.IsNullOrWhiteSpace(request.QrToken))
        {
            return AttendanceScanResult.Error("El enlace de asistencia no incluye el token de verificación QR.");
        }

        if (!_tokenService.TryValidateSessionQrToken(request.QrToken, out var sessionToken, out var errorMsg) || sessionToken == null)
        {
            return AttendanceScanResult.Error(errorMsg ?? "El código QR ha expirado o no es válido.");
        }

        if (sessionToken.SesionId != request.SesionId)
        {
            return AttendanceScanResult.Error("El código QR no corresponde a esta sesión de clase.");
        }

        // 2. Obtener la sesión (ListaAsistencia) y la materia
        var lista = await _listaRepo.ObtenerPorIdAsync(request.SesionId, ct);
        if (lista == null)
        {
            return AttendanceScanResult.Error("No se encontró la lista de asistencia solicitada.");
        }

        var materia = await _materiaRepo.ObtenerPorIdAsync(lista.MateriaId, ct);
        var materiaNombre = materia?.Nombre ?? "Materia";
        var materiaCodigo = materia?.Codigo ?? "";

        // 3. Validar estado de la sesión
        if (lista.Estado != "Abierta")
        {
            return AttendanceScanResult.Error($"La sesión de clase no está abierta para registro (Estado: {lista.Estado}).");
        }

        var isCacheActive = await _sessionCache.IsSessionActiveAsync(lista.Id, ct);
        if (!isCacheActive)
        {
            return AttendanceScanResult.Error("La ventana de 20 minutos para registrar asistencia ha concluido.");
        }

        // 4. Identificar o autenticar al Estudiante
        Estudiante? estudiante = null;
        Guid? dispositivoIdUsado = null;
        string? nuevaCookieDispositivo = null;

        // Intentar identificar mediante cookie criptográfica de dispositivo
        if (!string.IsNullOrWhiteSpace(request.DeviceToken) &&
            _tokenService.TryValidateDeviceCredential(request.DeviceToken, out var deviceToken, out _) &&
            deviceToken != null)
        {
            var disp = await _dispositivoRepo.ObtenerPorIdAsync(deviceToken.DispositivoId, ct);
            if (disp?.EstaRevocado is false)
            {
                estudiante = await _dispositivoRepo.ObtenerEstudiantePorDispositivoAsync(disp.Id, lista.InstitucionId, ct);
                if (estudiante != null)
                {
                    dispositivoIdUsado = disp.Id;
                    await _dispositivoRepo.ActualizarUltimoUsoAsync(disp.Id, ct);
                }
            }
        }

        // Si no se identificó el estudiante mediante dispositivo, verificar si se envió el formulario
        if (estudiante == null)
        {
            var codigoLimpio = request.Codigo?.Trim();
            var nombresLimpios = request.Nombres?.Trim();
            var apellidosLimpios = request.Apellidos?.Trim();
            var correoLimpio = request.Correo?.Trim();

            if (string.IsNullOrWhiteSpace(codigoLimpio) ||
                string.IsNullOrWhiteSpace(nombresLimpios) ||
                string.IsNullOrWhiteSpace(apellidosLimpios) ||
                string.IsNullOrWhiteSpace(correoLimpio) ||
                !request.AceptaTerminos)
            {
                return AttendanceScanResult.RegistroRequerido(materiaNombre, materiaCodigo);
            }

            // Buscar si ya existe por código en la institución o crear nuevo
            estudiante = await _estudianteRepo.ObtenerPorCodigoAsync(lista.InstitucionId, codigoLimpio, ct);
            if (estudiante == null)
            {
                estudiante = new Estudiante
                {
                    InstitucionId = lista.InstitucionId,
                    Codigo = codigoLimpio,
                    Nombres = nombresLimpios,
                    Apellidos = apellidosLimpios,
                    Correo = correoLimpio,
                    ConsentimientoEn = DateTimeOffset.UtcNow
                };
                estudiante = await _estudianteRepo.CrearOActualizarAsync(estudiante, ct);
            }

            // Emitir nueva credencial de dispositivo
            var nuevoDispId = Guid.NewGuid();
            var (tokenString, jtiHash) = _tokenService.GenerateDeviceCredential(nuevoDispId, lista.InstitucionId);

            var nuevoDisp = new Dispositivo
            {
                Id = nuevoDispId,
                JtiHash = jtiHash,
                Kid = "slac-key-v1",
                AgenteResumen = request.UserAgent
            };

            await _dispositivoRepo.RegistrarDispositivoAsync(nuevoDisp, estudiante.Id, lista.InstitucionId, ct);
            dispositivoIdUsado = nuevoDisp.Id;
            nuevaCookieDispositivo = tokenString;
        }

        // 5. Suscripción y Faltas Retroactivas (Paso 30)
        await ProcesarSuscripcionYFaltasRetroactivasAsync(lista, estudiante.Id, ct);

        // 6. Registrar Asistencia (Presente - Idempotente)
        var now = DateTimeOffset.UtcNow;
        var horaActual = DateTime.Now.TimeOfDay;
        var minutosDesdeInicio = (int)Math.Max(0, (horaActual - lista.HoraInicio).TotalMinutes);

        var detalleExistente = await _detalleRepo.ObtenerPorListaYEstudianteAsync(lista.Id, estudiante.Id, ct);
        if (detalleExistente?.Estado == "Presente")
        {
            return new AttendanceScanResult
            {
                Exito = true,
                YaRegistradoHoy = true,
                EstudianteId = estudiante.Id,
                EstudianteNombre = estudiante.NombreCompleto,
                EstudianteCodigo = estudiante.Codigo,
                MateriaNombre = materiaNombre,
                MateriaCodigo = materiaCodigo,
                HoraLlegada = detalleExistente.HoraLlegada ?? now,
                MinutosDesdeInicio = detalleExistente.MinutosDesdeInicio ?? minutosDesdeInicio,
                NuevaDeviceCookie = nuevaCookieDispositivo
            };
        }

        var nuevoDetalle = new AsistenciaDetalle
        {
            Id = detalleExistente?.Id ?? Guid.NewGuid(),
            InstitucionId = lista.InstitucionId,
            ListaId = lista.Id,
            EstudianteId = estudiante.Id,
            Estado = "Presente",
            Origen = "QR",
            HoraLlegada = now,
            MinutosDesdeInicio = minutosDesdeInicio,
            DispositivoId = dispositivoIdUsado
        };

        await _detalleRepo.RegistrarAsistenciaAsync(nuevoDetalle, ct);

        // 7. Incrementar contadores en Redis y actualizar totales en Postgres
        var totalPresentes = (int)await _sessionCache.IncrementAttendanceCounterAsync(lista.Id, ct);
        var detalleList = await _detalleRepo.ListarPorListaAsync(lista.Id, ct);
        var totalFaltas = detalleList.Count(x => x.Estado == "Falta");
        var suscritosCount = (await _suscripcionRepo.ListarEstudiantesIdsPorMateriaAsync(lista.MateriaId, ct)).Count;

        await _listaRepo.ActualizarTotalesAsync(lista.Id, suscritosCount, totalPresentes, totalFaltas, ct);

        // 8. Publicar evento en Redis Pub/Sub y SignalR
        var eventoAsistencia = new AttendanceEventMessage
        {
            TipoEvento = "asistencia_registrada",
            SesionId = lista.Id,
            TotalPresentes = totalPresentes,
            MinutosDesdeInicio = minutosDesdeInicio,
            EstudianteCodigo = estudiante.Codigo,
            Timestamp = now
        };
        await _eventBus.PublishEventAsync(eventoAsistencia, ct);
        await _notificationService.NotifyAttendanceRecordedAsync(lista.Id, totalPresentes, estudiante.Codigo, ct);

        _logger.LogInformation("Asistencia registrada con éxito: {Estudiante} ({Codigo}) para Sesión {ListaId} a las {Hora}.",
            estudiante.NombreCompleto, estudiante.Codigo, lista.Id, now);

        return new AttendanceScanResult
        {
            Exito = true,
            YaRegistradoHoy = false,
            EstudianteId = estudiante.Id,
            EstudianteNombre = estudiante.NombreCompleto,
            EstudianteCodigo = estudiante.Codigo,
            MateriaNombre = materiaNombre,
            MateriaCodigo = materiaCodigo,
            HoraLlegada = now,
            MinutosDesdeInicio = minutosDesdeInicio,
            NuevaDeviceCookie = nuevaCookieDispositivo
        };
    }

    private async Task ProcesarSuscripcionYFaltasRetroactivasAsync(ListaAsistencia lista, Guid estudianteId, CancellationToken ct)
    {
        var suscripcionExistente = await _suscripcionRepo.ObtenerAsync(lista.MateriaId, estudianteId, ct);
        if (suscripcionExistente != null)
        {
            return;
        }

        // 1. Crear suscripción a la materia
        var nuevaSuscripcion = new Suscripcion
        {
            InstitucionId = lista.InstitucionId,
            MateriaId = lista.MateriaId,
            EstudianteId = estudianteId,
            Fecha = DateTimeOffset.UtcNow,
            ListaOrigenId = lista.Id,
            Estado = "activa"
        };
        await _suscripcionRepo.SuscribirAsync(nuevaSuscripcion, ct);

        // 2. Faltas Retroactivas: Buscar sesiones pasadas 'Cerradas' de esta materia
        var listasPasadas = await _listaRepo.ListarPorMateriaAsync(lista.MateriaId, ct);
        var cerradasAnteriores = listasPasadas.Where(l => l.Fecha < lista.Fecha && l.Estado == "Cerrada").ToList();

        foreach (var cerrada in cerradasAnteriores)
        {
            var detalle = await _detalleRepo.ObtenerPorListaYEstudianteAsync(cerrada.Id, estudianteId, ct);
            if (detalle == null)
            {
                var faltaRetroactiva = new AsistenciaDetalle
                {
                    Id = Guid.NewGuid(),
                    InstitucionId = lista.InstitucionId,
                    ListaId = cerrada.Id,
                    EstudianteId = estudianteId,
                    Estado = "Falta",
                    Origen = "Suscripción tardía",
                    MinutosDesdeInicio = 0,
                    CreadoEn = DateTimeOffset.UtcNow
                };
                await _detalleRepo.RegistrarAsistenciaAsync(faltaRetroactiva, ct);

                // Actualizar totales de la lista pasada
                var detallesCerrada = await _detalleRepo.ListarPorListaAsync(cerrada.Id, ct);
                var faltasCerrada = detallesCerrada.Count(x => x.Estado == "Falta");
                var presentesCerrada = detallesCerrada.Count(x => x.Estado == "Presente");
                await _listaRepo.ActualizarTotalesAsync(cerrada.Id, cerrada.TotalSuscritos + 1, presentesCerrada, faltasCerrada, ct);
            }
        }
    }
}
