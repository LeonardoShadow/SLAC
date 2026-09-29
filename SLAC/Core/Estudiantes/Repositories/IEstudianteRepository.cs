using SLAC.Core.Estudiantes.Entities;

namespace SLAC.Core.Estudiantes.Repositories;

public interface IEstudianteRepository
{
    Task<Estudiante?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Estudiante?> ObtenerPorCodigoAsync(Guid institucionId, string codigo, CancellationToken ct = default);
    Task<Estudiante?> ObtenerPorCorreoAsync(Guid institucionId, string correo, CancellationToken ct = default);
    Task<Estudiante> CrearOActualizarAsync(Estudiante estudiante, CancellationToken ct = default);
    Task<IReadOnlyList<Estudiante>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default);
    Task<bool> EliminarAsync(Guid id, CancellationToken ct = default);
}
