namespace SLAC.Infrastructure.Data;

public interface IDatabaseHealthService
{
    bool IsHealthy { get; }
    DateTimeOffset UltimaComprobacion { get; }
    string? ErrorMessage { get; }
    Task<bool> VerificarSaludAsync(bool forzar = false);
}

