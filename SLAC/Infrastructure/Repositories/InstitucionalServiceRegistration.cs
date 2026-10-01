using Microsoft.Extensions.DependencyInjection;
using SLAC.Core.Institucional.Repositories;

namespace SLAC.Infrastructure.Repositories;

public static class InstitucionalServiceRegistration
{
    public static IServiceCollection AddInstitucionalInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IEspacioRepository, SupabaseEspacioRepository>();
        services.AddScoped<IPeriodoAcademicoRepository, SupabasePeriodoRepository>();
        services.AddScoped<IDiaNoLectivoRepository, SupabaseDiaNoLectivoRepository>();
        services.AddScoped<IInstitucionRepository, SupabaseInstitucionRepository>();
        services.AddScoped<IAdministradorInstitucionalRepository, SupabaseAdministradorInstitucionalRepository>();

        return services;
    }
}
