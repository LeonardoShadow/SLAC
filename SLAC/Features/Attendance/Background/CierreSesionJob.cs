using Microsoft.Extensions.Logging;
using Quartz;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using SLAC.Features.Attendance.Services;

namespace SLAC.Features.Attendance.Background;

/// <summary>
/// Background Job ejecutado a los 20 minutos de haber iniciado la clase (Paso 29).
/// Invalida la sesión en Redis, computa las faltas automáticas e idempotentes para los estudiantes
/// suscritos que no asistieron, actualiza los contadores en Postgres y notifica el cierre.
/// </summary>
[DisallowConcurrentExecution]
public class CierreSesionJob(
    IListaAsistenciaRepository listaRepo,
    IAsistenciaDetalleRepository detalleRepo,
    ISuscripcionRepository suscripcionRepo,
    ISessionCacheRepository sessionCache,
    ISessionEventBus eventBus,
    IClassroomNotificationService notificationService,
    ILogger<CierreSesionJob> logger) : IJob
{
    private readonly IListaAsistenciaRepository _listaRepo = listaRepo;
    private readonly IAsistenciaDetalleRepository _detalleRepo = detalleRepo;
    private readonly ISuscripcionRepository _suscripcionRepo = suscripcionRepo;
    private readonly ISessionCacheRepository _sessionCache = sessionCache;
    private readonly ISessionEventBus _eventBus = eventBus;
    private readonly IClassroomNotificationService _notificationService = notificationService;
    private readonly ILogger<CierreSesionJob> _logger = logger;

    public async Task Execute(IJobExecutionContext context)
    {
        var dataMap = context.MergedJobDataMap;
        if (!Guid.TryParse(dataMap.GetString("ListaId"), out var listaId) ||
            !Guid.TryParse(dataMap.GetString("MateriaId"), out var materiaId) ||
            !Guid.TryParse(dataMap.GetString("InstitucionId"), out var institucionId))
        {
            _logger.LogError("Parámetros insuficientes en JobDataMap para CierreSesionJob.");
            return;
        }

        _logger.LogInformation("Iniciando Cierre de Sesión para Lista {ListaId} (Materia: {MateriaId})", listaId, materiaId);

        // Si la sesión fue marcada como Suspendida por emergencia, no generar faltas (Paso 39)
        var lista = await _listaRepo.ObtenerPorIdAsync(listaId, context.CancellationToken);
        if (lista?.Estado == "Suspendida")
        {
            _logger.LogInformation("La sesión {ListaId} fue suspendida previamente de emergencia. Omitiendo generación de inasistencias.", listaId);
            return;
        }

        var horaCierre = DateTime.Now.TimeOfDay;

        // 1. Actualizar estado a 'Cerrada' con hora de cierre en Postgres
        await _listaRepo.ActualizarEstadoAsync(listaId, "Cerrada", horaCierre, context.CancellationToken);

        // 2. Obtener lista de estudiantes suscritos a la materia
        var estudiantesSuscritos = await _suscripcionRepo.ListarEstudiantesIdsPorMateriaAsync(materiaId, context.CancellationToken);

        // 3. Obtener registros que ya marcaron asistencia (Presentes)
        var registrosAsistencia = await _detalleRepo.ListarPorListaAsync(listaId, context.CancellationToken);
        var presentesIds = new HashSet<Guid>(registrosAsistencia.Where(r => r.Estado == "Presente").Select(r => r.EstudianteId));

        // 4. Identificar ausentes (Suscritos que NO tienen asistencia registrada)
        var ausentesIds = estudiantesSuscritos.Where(id => !presentesIds.Contains(id)).ToList();

        // 5. Insertar faltas de manera idempotente (RN-02: ON CONFLICT DO NOTHING)
        var faltasRegistradas = await _detalleRepo.RegistrarFaltasIdempotenteAsync(
            listaId,
            institucionId,
            ausentesIds,
            context.CancellationToken);

        _logger.LogInformation("Faltas automáticas computadas para Lista {ListaId}: {TotalFaltas} ausentes de {TotalSuscritos} suscritos.",
            listaId, faltasRegistradas, estudiantesSuscritos.Count);

        // 6. Actualizar totales consolidados en la lista maestra
        var totalPresentes = presentesIds.Count;
        var totalFaltas = registrosAsistencia.Count(r => r.Estado == "Falta") + faltasRegistradas;
        await _listaRepo.ActualizarTotalesAsync(listaId, estudiantesSuscritos.Count, totalPresentes, totalFaltas, context.CancellationToken);

        // 7. Invalidar clave efímera en Redis (cierre de ventana de escaneo)
        await _sessionCache.InvalidateSessionAsync(listaId, context.CancellationToken);

        // 8. Publicar evento 'sesion_cerrada' en Redis Pub/Sub
        var evento = new AttendanceEventMessage
        {
            TipoEvento = "sesion_cerrada",
            SesionId = listaId,
            TotalPresentes = totalPresentes,
            TotalFaltas = totalFaltas,
            Timestamp = DateTimeOffset.UtcNow
        };
        await _eventBus.PublishEventAsync(evento, context.CancellationToken);

        // 9. Notificar a la pantalla del aula vía SignalR
        await _notificationService.NotifySessionClosedAsync(listaId, totalPresentes, totalFaltas, context.CancellationToken);

        _logger.LogInformation("Sesión {ListaId} cerrada exitosamente. Clave en Redis invalidada.", listaId);
    }
}
