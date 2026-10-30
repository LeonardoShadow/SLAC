using Microsoft.Extensions.Logging;

namespace SLAC.Infrastructure.Data;

/// <summary>
/// Helper de resiliencia y reintentos estructurados para operaciones de persistencia en Supabase (REF-02).
/// Evita el silenciamiento ciego de errores y previene fallos por micro-cortes transitorios de red.
/// </summary>
public static class SupabaseResilienceHelper
{
    public static async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        ILogger logger,
        string operationName,
        int maxRetries = 2,
        int delayMs = 150,
        CancellationToken ct = default)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (attempt <= maxRetries && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "[SLAC-RESILIENCIA] Reintento #{Attempt} para '{Operation}' ante fallo transitorio.", attempt, operationName);
                try
                {
                    await Task.Delay(delayMs * attempt, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SLAC-PERSISTENCIA-ERROR] Error crítico de persistencia en Supabase en '{Operation}' tras {Attempts} intentos. Operando en modo degradado.", operationName, attempt);
                return; // Mantiene resiliencia en modo degradado pero con registro formal de error
            }
        }
    }
}
