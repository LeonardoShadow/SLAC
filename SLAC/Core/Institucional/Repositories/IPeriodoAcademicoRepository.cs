using SLAC.Core.Institucional.Entities;

namespace SLAC.Core.Institucional.Repositories;

public interface IPeriodoAcademicoRepository
{
    Task<List<PeriodoAcademico>> GetByInstitucionAsync(Guid institucionId, CancellationToken cancellationToken = default);
    Task<PeriodoAcademico?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PeriodoAcademico> CreateAsync(PeriodoAcademico periodo, CancellationToken cancellationToken = default);
    Task<PeriodoAcademico> UpdateAsync(PeriodoAcademico periodo, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
