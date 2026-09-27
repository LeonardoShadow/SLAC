using Microsoft.Extensions.Logging;
using Quartz;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Institucional.Repositories;

namespace SLAC.Features.Attendance.Background;

/// <summary>
/// Background Job de las 00:05 AM (SRS 4.5 y PRD).
/// Crea de forma anticipada las sesiones de asistencia del día para todas las materias activas
/// y programa los jobs de apertura (a la hora de inicio) y cierre (20 minutos después).
/// </summary>
[DisallowConcurrentExecution]
public class GeneracionDiariaJob(
    IMateriaRepository materiaRepo,
    IDiaNoLectivoRepository diaNoLectivoRepo,
    IPeriodoAcademicoRepository periodoRepo,
    IListaAsistenciaRepository listaRepo,
    ISchedulerFactory schedulerFactory,
    ILogger<GeneracionDiariaJob> logger) : IJob
{
    private readonly IMateriaRepository _materiaRepo = materiaRepo;
    private readonly IDiaNoLectivoRepository _diaNoLectivoRepo = diaNoLectivoRepo;
    private readonly IPeriodoAcademicoRepository _periodoRepo = periodoRepo;
    private readonly IListaAsistenciaRepository _listaRepo = listaRepo;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly ILogger<GeneracionDiariaJob> _logger = logger;

    public static readonly JobKey Key = new("GeneracionDiariaJob", "AttendanceGroup");

    public async Task Execute(IJobExecutionContext context)
    {
        var hoyFecha = DateOnly.FromDateTime(DateTime.Today);
        var diaSemana = DateTime.Today.DayOfWeek;

        _logger.LogInformation("Iniciando Generación Diaria de Sesiones para {Fecha} ({DiaSemana})", hoyFecha, diaSemana);

        var diaSigla = diaSemana switch
        {
            DayOfWeek.Monday => "L",
            DayOfWeek.Tuesday => "M",
            DayOfWeek.Wednesday => "X",
            DayOfWeek.Thursday => "J",
            DayOfWeek.Friday => "V",
            _ => null
        };

        if (diaSigla == null)
        {
            _logger.LogInformation("Fin de semana detectado ({DiaSemana}). No se programan sesiones de clase (RN-01).", diaSemana);
            return;
        }

        // Demo Institución (Multi-tenant)
        var institucionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // 1. Verificar si hoy es día no lectivo / feriado
        var esFeriado = await _diaNoLectivoRepo.IsDiaNoLectivoAsync(institucionId, hoyFecha, context.CancellationToken);
        if (esFeriado)
        {
            _logger.LogInformation("Hoy {Fecha} es día no lectivo para la institución {InstitucionId}. Omitiendo generación.", hoyFecha, institucionId);
            return;
        }

        // 2. Verificar periodo académico vigente
        var periodos = await _periodoRepo.GetByInstitucionAsync(institucionId, context.CancellationToken);
        var periodoVigente = periodos.FirstOrDefault(p => p.FechaInicio <= hoyFecha && hoyFecha <= p.FechaFin);
        if (periodoVigente == null)
        {
            _logger.LogInformation("No existe periodo académico vigente para la fecha {Fecha}. Omitiendo generación.", hoyFecha);
            return;
        }

        // 3. Consultar materias activas para el día de hoy
        var materiasHoy = await _materiaRepo.ListarActivasPorDiaAsync(institucionId, diaSigla, context.CancellationToken);
        _logger.LogInformation("Se encontraron {Total} materias programadas para hoy ({DiaSigla}).", materiasHoy.Count, diaSigla);

        var scheduler = await _schedulerFactory.GetScheduler(context.CancellationToken);

        foreach (var materia in materiasHoy)
        {
            // 4. Crear o recuperar la lista de asistencia en estado 'Programada' (Idempotencia)
            var listaExistente = await _listaRepo.ObtenerPorMateriaYFechaAsync(materia.Id, hoyFecha, context.CancellationToken);
            if (listaExistente == null)
            {
                var nuevaLista = new ListaAsistencia
                {
                    InstitucionId = institucionId,
                    MateriaId = materia.Id,
                    DocenteId = materia.DocenteId,
                    EspacioId = materia.EspacioId,
                    Fecha = hoyFecha,
                    HoraInicio = materia.HoraInicio,
                    Estado = "Programada"
                };

                listaExistente = await _listaRepo.CrearOActualizarAsync(nuevaLista, context.CancellationToken);
                _logger.LogInformation("Lista de asistencia {ListaId} creada para materia {Materia} ({HoraInicio})",
                    listaExistente.Id, materia.Codigo, materia.HoraInicio);
            }

            // 5. Programar Apertura a la hora exacta de la clase
            var inicioClase = hoyFecha.ToDateTime(TimeOnly.FromTimeSpan(materia.HoraInicio), DateTimeKind.Local);
            var cierreClase = inicioClase.AddMinutes(20);

            await ProgramarAperturaYCierreAsync(scheduler, listaExistente.Id, materia.Id, institucionId, inicioClase, cierreClase, context.CancellationToken);
        }

        _logger.LogInformation("Generación Diaria completada con éxito para la fecha {Fecha}.", hoyFecha);
    }

    private static async Task ProgramarAperturaYCierreAsync(
        IScheduler scheduler,
        Guid listaId,
        Guid materiaId,
        Guid institucionId,
        DateTimeOffset inicioClase,
        DateTimeOffset cierreClase,
        CancellationToken ct)
    {
        // Job de Apertura
        var aperturaJobData = new JobDataMap
        {
            { "ListaId", listaId.ToString() },
            { "MateriaId", materiaId.ToString() },
            { "InstitucionId", institucionId.ToString() }
        };

        var aperturaTrigger = TriggerBuilder.Create()
            .WithIdentity($"Apertura_{listaId}", "AttendanceTriggers")
            .UsingJobData(aperturaJobData)
            .StartAt(inicioClase > DateTimeOffset.UtcNow ? inicioClase : DateTimeOffset.UtcNow)
            .Build();

        var aperturaJob = JobBuilder.Create<AperturaSesionJob>()
            .WithIdentity($"Job_Apertura_{listaId}", "AttendanceJobs")
            .UsingJobData(aperturaJobData)
            .Build();

        if (!await scheduler.CheckExists(aperturaJob.Key, ct))
        {
            await scheduler.ScheduleJob(aperturaJob, aperturaTrigger, ct);
        }

        // Job de Cierre (20 minutos después)
        var cierreJobData = new JobDataMap
        {
            { "ListaId", listaId.ToString() },
            { "MateriaId", materiaId.ToString() },
            { "InstitucionId", institucionId.ToString() }
        };

        var cierreTrigger = TriggerBuilder.Create()
            .WithIdentity($"Cierre_{listaId}", "AttendanceTriggers")
            .UsingJobData(cierreJobData)
            .StartAt(cierreClase > DateTimeOffset.UtcNow ? cierreClase : DateTimeOffset.UtcNow)
            .Build();

        var cierreJob = JobBuilder.Create<CierreSesionJob>()
            .WithIdentity($"Job_Cierre_{listaId}", "AttendanceJobs")
            .UsingJobData(cierreJobData)
            .Build();

        if (!await scheduler.CheckExists(cierreJob.Key, ct))
        {
            await scheduler.ScheduleJob(cierreJob, cierreTrigger, ct);
        }
    }
}
