using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLAC.Core.Configuration;
using StackExchange.Redis;

namespace SLAC.Infrastructure.Redis;

/// <summary>
/// Proveedor de conexión resiliente para Redis.
/// Implementa degradación elegante (SRS 3.3): Si Redis no está disponible o falla,
/// la aplicación no detiene su arranque y permite operar con validación directa en base de datos.
/// </summary>
public class RedisConnectionProvider : IDisposable
{
    private readonly RedisOptions _options;
    private readonly ILogger<RedisConnectionProvider> _logger;
    private readonly Lazy<ConnectionMultiplexer?> _lazyConnection;

    public RedisConnectionProvider(
        IOptions<RedisOptions> options, 
        ILogger<RedisConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _lazyConnection = new Lazy<ConnectionMultiplexer?>(InitializeConnection);
    }

    public bool IsConnected => _lazyConnection.Value?.IsConnected ?? false;

    public IDatabase? GetDatabase()
    {
        var connection = _lazyConnection.Value;
        return connection?.IsConnected == true ? connection.GetDatabase() : null;
    }

    public ISubscriber? GetSubscriber()
    {
        var connection = _lazyConnection.Value;
        return connection?.IsConnected == true ? connection.GetSubscriber() : null;
    }

    private ConnectionMultiplexer? InitializeConnection()
    {
        try
        {
            var configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectRetry = 2;
            configOptions.ConnectTimeout = _options.ConnectRetryMs;

            var multiplexer = ConnectionMultiplexer.Connect(configOptions);
            _logger.LogInformation("Conexión inicializada con Redis en {Endpoint}", _options.ConnectionString);
            return multiplexer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar a la instancia de Redis ({Endpoint}). Operando en modo degradado.", _options.ConnectionString);
            return null;
        }
    }

    public void Dispose()
    {
        if (_lazyConnection.IsValueCreated && _lazyConnection.Value != null)
        {
            _lazyConnection.Value.Dispose();
        }
    }
}
