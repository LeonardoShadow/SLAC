namespace SLAC.Core.Configuration;

/// <summary>
/// Opciones de configuración para la conexión y comportamiento de Redis.
/// </summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Cadena de conexión a Redis (ej: "localhost:6379" o servicio gestionado en la nube).
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379,abortConnect=false";

    /// <summary>
    /// Prefijo de aislamiento de claves para evitar colisiones entre entornos.
    /// </summary>
    public string KeyPrefix { get; set; } = "slac";

    /// <summary>
    /// Tiempo de reintento de conexión automática en milisegundos.
    /// </summary>
    public int ConnectRetryMs { get; set; } = 3000;
}
