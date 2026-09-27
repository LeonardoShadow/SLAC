using Microsoft.Extensions.DependencyInjection;
using SLAC.Core.Attendance.Repositories;

namespace SLAC.Infrastructure.Repositories;

public static class AttendanceServiceRegistration
{
    public static IServiceCollection AddAttendanceInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IListaAsistenciaRepository, SupabaseListaAsistenciaRepository>();
        services.AddScoped<IAsistenciaDetalleRepository, SupabaseAsistenciaDetalleRepository>();
        services.AddScoped<ISuscripcionRepository, SupabaseSuscripcionRepository>();

        return services;
    }
}
