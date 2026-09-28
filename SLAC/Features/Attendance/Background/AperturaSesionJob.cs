using Microsoft.Extensions.Logging;
using Quartz;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Security;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using SLAC.Features.Attendance.Services;

namespace SLAC.Features.Attendance.Background;

/// <summary>
/// Background Job ejecutado a la hora de inicio de una materia (Paso 28).
/// Abre la sesión en PostgreSQL, emite el primer token QR (rotación 0) y lo almacena en Redis (TTL 20 min).
/// </summary>
[DisallowConcurrentExecution]
public class AperturaSesionJob(
    IListaAsistenciaRepository listaRepo,
    ITokenService tokenService,
    ISessionCacheRepository sessionCache,
    ISessionEventBus eventBus,
    IClassroomNotificationService notificationService,
    ILogger<AperturaSesionJob> logger) : IJob
{
    private readonly IListaAsistenciaRepository _listaRepo = listaRepo;
    private readonly ITokenService _tokenService = tokenService;
    private readonly ISessionCacheRepository _sessionCache = sessionCache;
    private readonly ISessionEventBus _eventBus = eventBus;
    private readonly IClassroomNotificationService _notificationService = notificationService;
    private readonly ILogger<AperturaSesionJob> _logger = logger;

    public async Task Execute(IJobExecutionContext context)
    {
        var dataMap = context.MergedJobDataMap;
        if (!Guid.TryParse(dataMap.GetString("ListaId"), out var listaId) ||
            !Guid.TryParse(dataMap.GetString("MateriaId"), out var materiaId) ||
            !Guid.TryParse(dataMap.GetString("InstitucionId"), out var institucionId))
        {
            _logger.LogError("Parámetros insuficientes en JobDataMap para AperturaSesionJob.");
            return;
        }

        _logger.LogInformation("Iniciando Apertura de Sesión para Lista {ListaId} (Materia: {MateriaId})", listaId, materiaId);

        // 1. Actualizar estado a 'Abierta' en Postgres
        await _listaRepo.ActualizarEstadoAsync(listaId, "Abierta", null, context.CancellationToken);

        // 2. Generar el primer Token QR firmado con rotación 0
        var now = DateTimeOffset.UtcNow;
        const int ventanaMinutos = 20;
        var tokenInicial = _tokenService.GenerateSessionQrToken(listaId, institucionId, now, ventanaMinutos, 0);

        // 3. Almacenar estado efímero en Redis con TTL de 20 minutos
        var state = new SessionEphemeralState
        {
            SesionId = listaId,
            InstitucionId = institucionId,
            MateriaId = materiaId,
            InicioVigencia = now,
            Vencimiento = now.AddMinutes(ventanaMinutos),
            RotacionIndex = 0,
            TokenActual = tokenInicial,
            ContadorAsistentes = 0,
            EstaAbierta = true
        };
        await _sessionCache.SetActiveSessionAsync(state, TimeSpan.FromMinutes(ventanaMinutos), context.CancellationToken);

        // 4. Publicar evento en Redis Pub/Sub
        var evento = new AttendanceEventMessage
        {
            TipoEvento = "sesion_iniciada",
            SesionId = listaId,
            Timestamp = now,
            NuevoToken = tokenInicial,
            RotacionIndex = 0,
            TotalPresentes = 0
        };
        await _eventBus.PublishEventAsync(evento, context.CancellationToken);

        // 5. Notificar a la pantalla del aula vía SignalR
        await _notificationService.NotifyQrRotatedAsync(listaId, tokenInicial, 0, context.CancellationToken);

        _logger.LogInformation("Sesión {ListaId} abierta exitosamente. Token QR inicial activo en Redis.", listaId);
    }
}
