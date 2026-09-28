using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SLAC.Core.Configuration;
using SLAC.Core.Session;
using SLAC.Features.Attendance.Services;

namespace SLAC.Infrastructure.Redis;

public static class RedisServiceRegistration
{
    public static IServiceCollection AddRedisInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));

        services.AddSingleton<RedisConnectionProvider>();
        services.AddSingleton<ISessionCacheRepository, RedisSessionCacheRepository>();
        services.AddSingleton<ISessionEventBus, RedisSessionEventBus>();
        services.AddScoped<IClassroomNotificationService, ClassroomNotificationService>();

        services.AddSignalR();

        return services;
    }
}
