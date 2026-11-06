using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Core.Security;
using SLAC.Core.Session;
using SLAC.Features.Attendance.Hubs;

namespace SLAC.Features.Attendance.Services;

public class RevinculacionService : IRevinculacionService
{
    private readonly IRevinculacionRepository _revinculacionRepo;
    private readonly IEstudianteRepository _estudianteRepo;
    private readonly IDispositivoRepository _dispositivoRepo;
    private readonly IListaAsistenciaRepository _listaRepo;
    private readonly IAuditoriaRepository _auditoriaRepo;
    private readonly IHubContext<AttendanceHub> _hubContext;
    private readonly ILogger<RevinculacionService> _logger;
    private readonly IAsistenciaDetalleRepository? _detalleRepo;
    private readonly ITokenService? _tokenService;
    private readonly ISessionCacheRepository? _sessionCache;

    public RevinculacionService(
        IRevinculacionRepository revinculacionRepo,
        IEstudianteRepository estudianteRepo,
        IDispositivoRepository dispositivoRepo,
        IListaAsistenciaRepository listaRepo,
        IAuditoriaRepository auditoriaRepo,
        IHubContext<AttendanceHub> hubContext,
        ILogger<RevinculacionService> logger,
        IAsistenciaDetalleRepository? detalleRepo = null,
        ITokenService? tokenService = null,
        ISessionCacheRepository? sessionCache = null)
    {
        _revinculacionRepo = revinculacionRepo;
        _estudianteRepo = estudianteRepo;
        _dispositivoRepo = dispositivoRepo;
        _listaRepo = listaRepo;
        _auditoriaRepo = auditoriaRepo;
        _hubContext = hubContext;
        _logger = logger;
        _detalleRepo = detalleRepo;
        _tokenService = tokenService;
        _sessionCache = sessionCache;
    }

    public async Task<SolicitudRevinculacionResult> SolicitarRevinculacionAsync(
        Guid sesionId,
        string codigoEstudiante,
        string userAgent,
        CancellationToken ct = default)
    {
        var lista = await _listaRepo.ObtenerPorIdAsync(sesionId, ct);
        if (lista == null)
        {
            return new SolicitudRevinculacionResult(false, false, null, "La sesión de asistencia no existe.", null, null);
        }

        var estudiante = await _estudianteRepo.ObtenerPorCodigoAsync(lista.InstitucionId, codigoEstudiante.Trim(), ct);
        if (estudiante == null && codigoEstudiante.Contains('@'))
        {
            estudiante = await _estudianteRepo.ObtenerPorCorreoAsync(lista.InstitucionId, codigoEstudiante.Trim(), ct);
        }
        if (estudiante == null)
        {
            var todos = await _estudianteRepo.ListarPorInstitucionAsync(lista.InstitucionId, ct);
            estudiante = todos.FirstOrDefault(e =>
                e.Codigo.Equals(codigoEstudiante, StringComparison.OrdinalIgnoreCase) ||
                e.Correo.Equals(codigoEstudiante, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(e.DocumentoIdentidad) && e.DocumentoIdentidad.Equals(codigoEstudiante, StringComparison.OrdinalIgnoreCase)));
        }

        if (estudiante == null)
        {
            return new SolicitudRevinculacionResult(false, false, null, "Estudiante no encontrado en la institución.", null, null);
        }

        // Verificar si ya existe una solicitud pendiente
        var existente = await _revinculacionRepo.ObtenerPendientePorEstudianteYMateriaAsync(estudiante.Id, lista.MateriaId, ct);
        if (existente != null)
        {
            existente.DispositivoAgente = userAgent;
            existente.EstudianteNombre = estudiante.NombreCompleto;
            existente.EstudianteCodigo = estudiante.Codigo;
            existente.ExpiraEn = DateTimeOffset.UtcNow.AddHours(2);
            existente.CreadoEn = DateTimeOffset.UtcNow;

            return new SolicitudRevinculacionResult(
                true,
                false,
                existente.Id,
                "Solicitud de revinculación en espera. Tu docente puede autorizar tu nuevo dispositivo en pantalla.",
                estudiante.NombreCompleto,
                estudiante.Codigo);
        }

        // Crear nueva solicitud de revinculación
        var solicitud = new Revinculacion
        {
            Id = Guid.NewGuid(),
            InstitucionId = lista.InstitucionId,
            EstudianteId = estudiante.Id,
            MateriaId = lista.MateriaId,
            DocenteId = lista.DocenteId,
            ExpiraEn = DateTimeOffset.UtcNow.AddHours(2),
            EstudianteNombre = estudiante.NombreCompleto,
            EstudianteCodigo = estudiante.Codigo,
            DispositivoAgente = userAgent
        };

        await _revinculacionRepo.CrearSolicitudAsync(solicitud, ct);

        // Notificar en tiempo real al docente en la pantalla de proyección (SignalR)
        try
        {
            var groupName = AttendanceHub.GetGroupName(sesionId.ToString());
            await _hubContext.Clients.Group(groupName)
                .SendAsync("NuevaSolicitudRevinculacion",
                    solicitud.Id.ToString(),
                    estudiante.NombreCompleto,
                    estudiante.Codigo,
                    DateTime.Now.ToString("HH:mm:ss"),
                    cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitiendo notificación SignalR de revinculación para sesión {SesionId}", sesionId);
        }

        _logger.LogInformation("Solicitud de revinculación {Id} creada para estudiante {Codigo} en materia {MateriaId}",
            solicitud.Id, estudiante.Codigo, lista.MateriaId);

        return new SolicitudRevinculacionResult(
            true,
            false,
            solicitud.Id,
            "Solicitud enviada con éxito. Tu docente puede autorizar tu nuevo dispositivo en pantalla.",
            estudiante.NombreCompleto,
            estudiante.Codigo);
    }

    public async Task<(bool Exito, string? Error)> AutorizarRevinculacionAsync(
        Guid solicitudId,
        string actorDocente,
        CancellationToken ct = default)
    {
        var solicitud = await _revinculacionRepo.ObtenerPorIdAsync(solicitudId, ct);
        if (solicitud == null)
        {
            return (false, "No se encontró la solicitud de revinculación.");
        }

        if (solicitud.UsadaEn.HasValue)
        {
            return (false, "Esta solicitud ya fue autorizada y procesada anteriormente.");
        }

        if (solicitud.ExpiraEn < DateTimeOffset.UtcNow)
        {
            return (false, "La solicitud ha expirado (plazo máximo de 2 horas).");
        }

        // 1. Marcar solicitud como autorizada/usada
        await _revinculacionRepo.MarcarComoUsadaAsync(solicitudId, ct);

        // Limpiar cualquier otra solicitud pendiente residual que pudiera tener el mismo estudiante
        try
        {
            var pendientesEstudiante = await _revinculacionRepo.ListarPendientesPorMateriaAsync(solicitud.MateriaId, ct);
            foreach (var residual in pendientesEstudiante.Where(r => r.EstudianteId == solicitud.EstudianteId && r.Id != solicitudId))
            {
                await _revinculacionRepo.MarcarComoUsadaAsync(residual.Id, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error limpiando solicitudes residuales del estudiante {EstudianteId}", solicitud.EstudianteId);
        }

        // 2. Revocar el dispositivo previo del estudiante si existía (RN-05)
        var vincActiva = await _dispositivoRepo.ObtenerVinculacionActivaAsync(solicitud.EstudianteId, solicitud.InstitucionId, ct);
        if (vincActiva != null)
        {
            await _dispositivoRepo.RevocarDispositivoAsync(vincActiva.DispositivoId, ct);
            _logger.LogInformation("Dispositivo anterior {DispId} revocado para estudiante {EstudianteId}",
                vincActiva.DispositivoId, solicitud.EstudianteId);
        }

        // 3. Registrar de inmediato el nuevo dispositivo para el estudiante si el servicio de tokens está disponible
        var nuevoDispId = Guid.NewGuid();
        if (_tokenService != null)
        {
            try
            {
                var (_, jtiHash) = _tokenService.GenerateDeviceCredential(nuevoDispId, solicitud.InstitucionId);
                var nuevoDisp = new Dispositivo
                {
                    Id = nuevoDispId,
                    JtiHash = jtiHash,
                    Kid = "slac-master-k1",
                    AgenteResumen = !string.IsNullOrWhiteSpace(solicitud.DispositivoAgente) ? solicitud.DispositivoAgente : "Dispositivo Autorizado por Docente"
                };
                await _dispositivoRepo.RegistrarDispositivoAsync(nuevoDisp, solicitud.EstudianteId, solicitud.InstitucionId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error registrando nuevo dispositivo autorizado para estudiante {EstudianteId}", solicitud.EstudianteId);
            }
        }

        // 4. Inscribir y registrar asistencia en vivo en la sesión de clase activa hoy
        ListaAsistencia? sesionHoy = null;
        try
        {
            sesionHoy = await _listaRepo.ObtenerPorMateriaYFechaAsync(solicitud.MateriaId, DateOnly.FromDateTime(DateTime.UtcNow), ct);
            if (sesionHoy == null || sesionHoy.Estado != "Abierta")
            {
                var todas = await _listaRepo.ListarPorMateriaAsync(solicitud.MateriaId, ct);
                sesionHoy = todas.FirstOrDefault(l => l.Estado == "Abierta");
            }

            if (sesionHoy != null && _detalleRepo != null)
            {
                var est = await _estudianteRepo.ObtenerPorIdAsync(solicitud.EstudianteId, ct);
                var estNombre = est?.NombreCompleto ?? solicitud.EstudianteNombre ?? "Estudiante";
                var estCodigo = est?.Codigo ?? solicitud.EstudianteCodigo ?? "";

                var detalleExistente = await _detalleRepo.ObtenerPorListaYEstudianteAsync(sesionHoy.Id, solicitud.EstudianteId, ct);
                if (detalleExistente == null)
                {
                    var horaInicio = sesionHoy.HoraInicio;
                    var minInicio = (int)Math.Max(0, (DateTime.Now.TimeOfDay - horaInicio).TotalMinutes);
                    var detalle = new AsistenciaDetalle
                    {
                        Id = Guid.NewGuid(),
                        InstitucionId = sesionHoy.InstitucionId,
                        ListaId = sesionHoy.Id,
                        EstudianteId = solicitud.EstudianteId,
                        Estado = "Presente",
                        Origen = "Revinculacion",
                        DispositivoId = nuevoDispId,
                        HoraLlegada = DateTimeOffset.UtcNow,
                        MinutosDesdeInicio = minInicio
                    };
                    await _detalleRepo.RegistrarAsistenciaAsync(detalle, ct);
                }
                else if (detalleExistente.Estado != "Presente")
                {
                    detalleExistente.Estado = "Presente";
                    detalleExistente.DispositivoId = nuevoDispId;
                    detalleExistente.HoraLlegada ??= DateTimeOffset.UtcNow;
                    await _detalleRepo.RegistrarAsistenciaAsync(detalleExistente, ct);
                }

                var todosDetalles = await _detalleRepo.ListarPorListaAsync(sesionHoy.Id, ct);
                var totalPres = todosDetalles.Count(d => d.Estado == "Presente");
                var totalFalt = todosDetalles.Count(d => d.Estado == "Falta");
                var totalSuscritos = todosDetalles.Count;
                await _listaRepo.ActualizarTotalesAsync(sesionHoy.Id, totalSuscritos, totalPres, totalFalt, ct);

                if (_sessionCache != null)
                {
                    try
                    {
                        await _sessionCache.TryClaimAttendanceAsync(
                            sesionHoy.Id,
                            solicitud.EstudianteId,
                            nuevoDispId.ToString(),
                            null,
                            validatePresenceCode: false,
                            cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error sincronizando caché Redis para revinculación autorizada.");
                    }
                }

                // Notificar asistencia en tiempo real a la pantalla proyectada (SignalR)
                var sesionGroupName = AttendanceHub.GetGroupName(sesionHoy.Id.ToString());
                await _hubContext.Clients.Group(sesionGroupName)
                    .SendAsync("AsistenciaRegistrada", estNombre, estCodigo, totalPres, cancellationToken: ct);
                await _hubContext.Clients.Group(sesionGroupName)
                    .SendAsync("AsistenciaActualizada", totalPres, estCodigo, cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registrando asistencia automática en sesión activa tras autorizar revinculación");
        }

        // 5. Registrar auditoría inmutable
        var auditoria = new Auditoria
        {
            InstitucionId = solicitud.InstitucionId,
            Actor = actorDocente,
            Evento = "revinculacion_dispositivo",
            Entidad = "dispositivo_estudiante",
            DatosJson = JsonSerializer.Serialize(new
            {
                solicitudId,
                estudianteId = solicitud.EstudianteId,
                materiaId = solicitud.MateriaId,
                dispositivoAnteriorRevocado = vincActiva?.DispositivoId,
                nuevoDispositivoId = nuevoDispId,
                autorizadoPor = actorDocente,
                fecha = DateTime.UtcNow
            })
        };
        await _auditoriaRepo.RegistrarEventoAsync(auditoria, ct);

        // 6. Notificar vía SignalR que la revinculación fue aprobada a la sala de clase correspondiente (REF-08)
        try
        {
            if (sesionHoy != null)
            {
                var groupName = AttendanceHub.GetGroupName(sesionHoy.Id.ToString());
                await _hubContext.Clients.Group(groupName)
                    .SendAsync("RevinculacionAprobada", solicitud.EstudianteId.ToString(), cancellationToken: ct);
            }
            else
            {
                var materiaGroupName = $"materia_{solicitud.MateriaId}";
                await _hubContext.Clients.Group(materiaGroupName)
                    .SendAsync("RevinculacionAprobada", solicitud.EstudianteId.ToString(), cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitiendo evento SignalR RevinculacionAprobada para materia {MateriaId}.", solicitud.MateriaId);
        }

        _logger.LogInformation("Revinculación {Id} autorizada exitosamente por {Docente}", solicitudId, actorDocente);
        return (true, null);
    }

    public async Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
    {
        var lista = await _revinculacionRepo.ListarPendientesPorMateriaAsync(materiaId, ct);

        // Enriquecer con nombres de estudiantes si no vinieron cargados
        foreach (var rev in lista)
        {
            if (string.IsNullOrWhiteSpace(rev.EstudianteNombre))
            {
                var est = await _estudianteRepo.ObtenerPorIdAsync(rev.EstudianteId, ct);
                if (est != null)
                {
                    rev.EstudianteNombre = est.NombreCompleto;
                    rev.EstudianteCodigo = est.Codigo;
                }
            }
        }

        // Deduplicar estrictamente por estudiante: 1 única solicitud (la más reciente) por estudiante
        var unicos = lista
            .GroupBy(r => r.EstudianteId)
            .Select(g => g.OrderByDescending(r => r.CreadoEn).First())
            .OrderByDescending(r => r.CreadoEn)
            .ToList();

        return unicos;
    }
}
