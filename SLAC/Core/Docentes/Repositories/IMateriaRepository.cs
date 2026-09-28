using SLAC.Core.Docentes.Entities;

namespace SLAC.Core.Docentes.Repositories;

public interface IMateriaRepository
{
    Task<IReadOnlyList<Materia>> ListarPorDocenteAsync(Guid docenteId, CancellationToken ct = default);
    Task<IReadOnlyList<Materia>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default);
    Task<IReadOnlyList<Materia>> ListarActivasPorDiaAsync(Guid institucionId, string diaSigla, CancellationToken ct = default);
    Task<Materia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Materia> GuardarAsync(Materia materia, CancellationToken ct = default);
    Task<bool> ArchivarAsync(Guid id, CancellationToken ct = default);
}
