using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SLAC.Core.Configuration;

namespace SLAC.Infrastructure.Data;

public static class SupabaseServiceRegistration
{
    public static IServiceCollection AddSupabaseInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SlacSupabaseOptions>(configuration.GetSection(SlacSupabaseOptions.SectionName));

        services.AddHttpClient<DatabaseHealthService>();
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

        // REF-01: Supabase.Client como Singleton sin bloqueo sincrónico en resolución de dependencias
        services.AddSingleton<Supabase.Client>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<SlacSupabaseOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.Url) || string.IsNullOrWhiteSpace(options.Key) || options.Url.Contains("TU_PROYECTO"))
            {
                // Fallback seguro en desarrollo si aún no se han configurado credenciales reales
                return new Supabase.Client("https://placeholder.supabase.co", "placeholder-key");
            }

            var supabaseOptions = new Supabase.SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            };

            return new Supabase.Client(options.Url, options.Key, supabaseOptions);
        });

        // Inicialización asincrónica sin bloqueo de hilos (IHostedService)
        services.AddHostedService<SupabaseInitializerHostedService>();

        return services;
    }
}
