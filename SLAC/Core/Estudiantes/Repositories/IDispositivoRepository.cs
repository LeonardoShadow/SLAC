using SLAC.Core.Estudiantes.Entities;

namespace SLAC.Core.Estudiantes.Repositories;

public interface IDispositivoRepository
{
    Task<Dispositivo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Dispositivo?> ObtenerPorJtiHashAsync(string jtiHash, CancellationToken ct = default);
    Task<Dispositivo> RegistrarDispositivoAsync(Dispositivo dispositivo, Guid estudianteId, Guid institucionId, CancellationToken ct = default);
    Task<Estudiante?> ObtenerEstudiantePorDispositivoAsync(Guid dispositivoId, Guid institucionId, CancellationToken ct = default);
    Task ActualizarUltimoUsoAsync(Guid dispositivoId, CancellationToken ct = default);
    Task RevocarDispositivoAsync(Guid dispositivoId, CancellationToken ct = default);
    Task<DispositivoEstudiante?> ObtenerVinculacionActivaAsync(Guid estudianteId, Guid institucionId, CancellationToken ct = default);
}
