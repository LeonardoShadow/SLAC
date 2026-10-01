using SLAC.Core.Institucional.Entities;

namespace SLAC.Core.Institucional.Repositories;

public interface IInstitucionRepository
{
    Task<IReadOnlyList<Institucion>> ListarTodasAsync(CancellationToken ct = default);
    Task<Institucion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Institucion> GuardarAsync(Institucion institucion, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(Guid id, string nuevoEstado, CancellationToken ct = default);
}
