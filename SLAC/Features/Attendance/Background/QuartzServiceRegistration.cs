using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace SLAC.Features.Attendance.Background;

public static class QuartzServiceRegistration
{
    public static IServiceCollection AddQuartzBackgroundScheduler(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            // Registrar Job de Generación Diaria a las 00:05 AM
            q.AddJob<GeneracionDiariaJob>(opts => opts.WithIdentity(GeneracionDiariaJob.Key));

            q.AddTrigger(opts => opts
                .ForJob(GeneracionDiariaJob.Key)
                .WithIdentity("Trigger_GeneracionDiaria_0005", "AttendanceTriggers")
                .WithCronSchedule("0 5 0 ? * *")); // Ejecutar a las 00:05:00 todos los días
        });

        // Habilitar Hosted Service de Quartz integrado en el ciclo de vida de ASP.NET Core
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
