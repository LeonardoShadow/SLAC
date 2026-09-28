using SLAC.Core.Docentes.Entities;

namespace SLAC.Core.Docentes.Repositories;

public interface IDocenteRepository
{
    Task<IReadOnlyList<Docente>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default);
    Task<Docente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Docente?> ObtenerPorUsuarioIdAsync(Guid userId, CancellationToken ct = default);
    Task<Docente> GuardarAsync(Docente docente, CancellationToken ct = default);
}
