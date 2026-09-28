using SLAC.Core.Institucional.Entities;

namespace SLAC.Core.Institucional.Repositories;

public interface IDiaNoLectivoRepository
{
    Task<List<DiaNoLectivo>> GetByInstitucionAsync(Guid institucionId, CancellationToken cancellationToken = default);
    Task<bool> IsDiaNoLectivoAsync(Guid institucionId, DateOnly fecha, CancellationToken cancellationToken = default);
    Task<DiaNoLectivo> CreateAsync(DiaNoLectivo diaNoLectivo, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
