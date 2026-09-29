using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Features.Attendance.Hubs;

namespace SLAC.Features.Attendance.Services;

public class RevinculacionService(
    IRevinculacionRepository revinculacionRepo,
    IEstudianteRepository estudianteRepo,
    IDispositivoRepository dispositivoRepo,
    IListaAsistenciaRepository listaRepo,
    IAuditoriaRepository auditoriaRepo,
    IHubContext<AttendanceHub> hubContext,
    ILogger<RevinculacionService> logger) : IRevinculacionService
{
    private readonly IRevinculacionRepository _revinculacionRepo = revinculacionRepo;
    private readonly IEstudianteRepository _estudianteRepo = estudianteRepo;
    private readonly IDispositivoRepository _dispositivoRepo = dispositivoRepo;
    private readonly IListaAsistenciaRepository _listaRepo = listaRepo;
    private readonly IAuditoriaRepository _auditoriaRepo = auditoriaRepo;
    private readonly IHubContext<AttendanceHub> _hubContext = hubContext;
    private readonly ILogger<RevinculacionService> _logger = logger;

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
            return new SolicitudRevinculacionResult(
                true,
                false,
                existente.Id,
                "Ya tienes una solicitud de revinculación en espera. Solicita al docente su aprobación en aula.",
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

        // 2. Revocar el dispositivo previo del estudiante si existía (RN-05)
        var vincActiva = await _dispositivoRepo.ObtenerVinculacionActivaAsync(solicitud.EstudianteId, solicitud.InstitucionId, ct);
        if (vincActiva != null)
        {
            await _dispositivoRepo.RevocarDispositivoAsync(vincActiva.DispositivoId, ct);
            _logger.LogInformation("Dispositivo anterior {DispId} revocado para estudiante {EstudianteId}",
                vincActiva.DispositivoId, solicitud.EstudianteId);
        }

        // 3. Registrar auditoría inmutable
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
                autorizadoPor = actorDocente,
                fecha = DateTime.UtcNow
            })
        };
        await _auditoriaRepo.RegistrarEventoAsync(auditoria, ct);

        // 4. Notificar vía SignalR que la revinculación fue aprobada
        try
        {
            await _hubContext.Clients.All
                .SendAsync("RevinculacionAprobada", solicitud.EstudianteId.ToString(), cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error emitiendo evento SignalR RevinculacionAprobada.");
        }

        _logger.LogInformation("Revinculación {Id} autorizada exitosamente por {Docente}", solicitudId, actorDocente);
        return (true, null);
    }

    public async Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
    {
        var lista = await _revinculacionRepo.ListarPendientesPorMateriaAsync(materiaId, ct);

        // Enriquecer con nombres de estudiantes
        foreach (var rev in lista)
        {
            var est = await _estudianteRepo.ObtenerPorIdAsync(rev.EstudianteId, ct);
            if (est != null)
            {
                rev.EstudianteNombre = est.NombreCompleto;
                rev.EstudianteCodigo = est.Codigo;
            }
        }

        return lista;
    }
}
