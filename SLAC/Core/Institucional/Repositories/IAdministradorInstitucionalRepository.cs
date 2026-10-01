using SLAC.Core.Institucional.Entities;

namespace SLAC.Core.Institucional.Repositories;

public interface IAdministradorInstitucionalRepository
{
    Task<IReadOnlyList<AdministradorInstitucional>> ListarTodosAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdministradorInstitucional>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default);
    Task<AdministradorInstitucional?> ValidarCredencialesAsync(string correo, string codigoPin, CancellationToken ct = default);
    Task<AdministradorInstitucional> GuardarAsync(AdministradorInstitucional admin, CancellationToken ct = default);
    Task<bool> CambiarEstadoAsync(Guid id, bool activo, CancellationToken ct = default);
}
