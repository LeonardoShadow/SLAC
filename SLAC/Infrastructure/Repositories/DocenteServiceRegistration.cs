using Microsoft.Extensions.DependencyInjection;
using SLAC.Core.Docentes.Repositories;

namespace SLAC.Infrastructure.Repositories;

public static class DocenteServiceRegistration
{
    public static IServiceCollection AddDocenteInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IDocenteRepository, SupabaseDocenteRepository>();
        services.AddScoped<IMateriaRepository, SupabaseMateriaRepository>();

        return services;
    }
}
