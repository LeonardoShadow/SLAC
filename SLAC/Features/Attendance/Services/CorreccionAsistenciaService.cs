using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Session;
using SLAC.Features.Attendance.Hubs;

namespace SLAC.Features.Attendance.Services;

public class CorreccionAsistenciaService(
    IListaAsistenciaRepository listaRepo,
    IAsistenciaDetalleRepository detalleRepo,
    IAuditoriaRepository auditoriaRepo,
    ISessionCacheRepository sessionCache,
    IHubContext<AttendanceHub> hubContext,
    ILogger<CorreccionAsistenciaService> logger) : ICorreccionAsistenciaService
{
    private readonly IListaAsistenciaRepository _listaRepo = listaRepo;
    private readonly IAsistenciaDetalleRepository _detalleRepo = detalleRepo;
    private readonly IAuditoriaRepository _auditoriaRepo = auditoriaRepo;
    private readonly ISessionCacheRepository _sessionCache = sessionCache;
    private readonly IHubContext<AttendanceHub> _hubContext = hubContext;
    private readonly ILogger<CorreccionAsistenciaService> _logger = logger;

    public async Task<(bool Exito, string? Error)> CorregirAsistenciaAsync(
        Guid listaId,
        Guid estudianteId,
        string nuevoEstado,
        string motivo,
        string actorDocente,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
        {
            return (false, "Debe especificar un motivo válido de justificación (mínimo 5 caracteres).");
        }

        if (nuevoEstado != "Presente" && nuevoEstado != "Falta")
        {
            return (false, "El estado especificado no es válido (solo 'Presente' o 'Falta').");
        }

        var lista = await _listaRepo.ObtenerPorIdAsync(listaId, ct);
        if (lista == null)
        {
            return (false, "No se encontró la sesión de clase solicitada.");
        }

        var detalle = await _detalleRepo.ObtenerPorListaYEstudianteAsync(listaId, estudianteId, ct);
        if (detalle == null)
        {
            return (false, "No se encontró el registro de asistencia para este estudiante en la sesión.");
        }

        var estadoAnterior = detalle.Estado;
        if (estadoAnterior == nuevoEstado)
        {
            return (true, null); // No hay cambios requeridos
        }

        // 1. Actualizar el detalle de asistencia con origen = 'Corrección'
        detalle.Estado = nuevoEstado;
        detalle.Origen = "Corrección";
        await _detalleRepo.RegistrarAsistenciaAsync(detalle, ct);

        // 2. Ajustar contadores de la sesión
        if (estadoAnterior == "Falta" && nuevoEstado == "Presente")
        {
            lista.TotalPresentes++;
            lista.TotalFaltas = Math.Max(0, lista.TotalFaltas - 1);
        }
        else if (estadoAnterior == "Presente" && nuevoEstado == "Falta")
        {
            lista.TotalPresentes = Math.Max(0, lista.TotalPresentes - 1);
            lista.TotalFaltas++;
        }
        await _listaRepo.CrearOActualizarAsync(lista, ct);

        // 3. Registrar auditoría inmutable
        var auditoria = new Auditoria
        {
            InstitucionId = lista.InstitucionId,
            Actor = actorDocente,
            Evento = "correccion_asistencia",
            Entidad = "asistencia_detalle",
            DatosJson = JsonSerializer.Serialize(new
            {
                detalleId = detalle.Id,
                listaId = lista.Id,
                materiaId = lista.MateriaId,
                estudianteId,
                estadoAnterior,
                nuevoEstado,
                motivo = motivo.Trim(),
                fecha = DateTime.UtcNow
            })
        };
        await _auditoriaRepo.RegistrarEventoAsync(auditoria, ct);

        _logger.LogInformation("Asistencia corregida para estudiante {EstudianteId} en lista {ListaId} por {Actor}: {Antiguo} -> {Nuevo}",
            estudianteId, listaId, actorDocente, estadoAnterior, nuevoEstado);

        return (true, null);
    }

    public async Task<(bool Exito, string? Error)> SuspenderClaseEmergenciaAsync(
        Guid listaId,
        string motivo,
        string actorDocente,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
        {
            return (false, "Debe ingresar el motivo de suspensión de emergencia (mínimo 5 caracteres).");
        }

        var lista = await _listaRepo.ObtenerPorIdAsync(listaId, ct);
        if (lista == null)
        {
            return (false, "No se encontró la lista de clase a suspender.");
        }

        // 1. Cambiar estado a 'Suspendida'
        await _listaRepo.ActualizarEstadoAsync(listaId, "Suspendida", DateTime.Now.TimeOfDay, ct);

        // 2. Anular el token de sesión en Redis para evitar más escaneos
        await _sessionCache.InvalidateSessionAsync(listaId, ct);

        // 3. Notificar vía SignalR a pantallas de proyección conectadas
        var groupName = AttendanceHub.GetGroupName(listaId.ToString());
        await _hubContext.Clients.Group(groupName)
            .SendAsync("SesionSuspendida", listaId.ToString(), motivo.Trim(), cancellationToken: ct);

        // 4. Registrar auditoría inmutable
        var auditoria = new Auditoria
        {
            InstitucionId = lista.InstitucionId,
            Actor = actorDocente,
            Evento = "suspension_clase",
            Entidad = "lista_asistencia",
            DatosJson = JsonSerializer.Serialize(new
            {
                listaId,
                materiaId = lista.MateriaId,
                motivo = motivo.Trim(),
                totalPresentesAlMomento = lista.TotalPresentes,
                fecha = DateTime.UtcNow
            })
        };
        await _auditoriaRepo.RegistrarEventoAsync(auditoria, ct);

        _logger.LogInformation("Sesión de clase {ListaId} suspendida de emergencia por {Actor}. Motivo: {Motivo}",
            listaId, actorDocente, motivo);

        return (true, null);
    }
}
