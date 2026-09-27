using Microsoft.Extensions.DependencyInjection;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Features.Attendance.Services;

namespace SLAC.Infrastructure.Repositories;

public static class AttendanceServiceRegistration
{
    public static IServiceCollection AddAttendanceInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IListaAsistenciaRepository, SupabaseListaAsistenciaRepository>();
        services.AddScoped<IAsistenciaDetalleRepository, SupabaseAsistenciaDetalleRepository>();
        services.AddScoped<ISuscripcionRepository, SupabaseSuscripcionRepository>();
        services.AddScoped<ISuscripcionService, SuscripcionService>();

        return services;
    }

    public static IServiceCollection AddEstudianteInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IEstudianteRepository, SupabaseEstudianteRepository>();
        services.AddScoped<IDispositivoRepository, SupabaseDispositivoRepository>();

        return services;
    }
}
