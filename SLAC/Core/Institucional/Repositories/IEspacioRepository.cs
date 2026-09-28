using SLAC.Core.Institucional.Entities;

namespace SLAC.Core.Institucional.Repositories;

public interface IEspacioRepository
{
    Task<List<Espacio>> GetByInstitucionAsync(Guid institucionId, CancellationToken cancellationToken = default);
    Task<Espacio?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Espacio> CreateAsync(Espacio espacio, CancellationToken cancellationToken = default);
    Task<Espacio> UpdateAsync(Espacio espacio, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
