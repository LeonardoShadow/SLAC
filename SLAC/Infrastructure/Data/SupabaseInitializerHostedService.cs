using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SLAC.Infrastructure.Data;

/// <summary>
/// Inicializador asincrónico para Supabase.Client en el arranque de la aplicación (REF-01).
/// Elimina por completo el anti-patrón de bloqueo de hilos (Sync-over-Async) en la inyección de dependencias.
/// </summary>
public class SupabaseInitializerHostedService(
    Supabase.Client supabaseClient,
    ILogger<SupabaseInitializerHostedService> logger) : IHostedService
{
    private readonly Supabase.Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseInitializerHostedService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Inicializando cliente Supabase de forma asincrónica en background...");
            await _supabaseClient.InitializeAsync();
            _logger.LogInformation("Cliente Supabase inicializado correctamente sin bloqueo de hilos.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Aviso: No se pudo conectar con Supabase durante el arranque asincrónico. Operando en modo resiliente.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
