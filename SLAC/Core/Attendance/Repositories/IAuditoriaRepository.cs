using SLAC.Core.Attendance.Entities;

namespace SLAC.Core.Attendance.Repositories;

/// <summary>
/// Contrato para persistencia de registros inmutables de auditoría en PostgreSQL/Supabase.
/// </summary>
public interface IAuditoriaRepository
{
    Task RegistrarEventoAsync(Auditoria auditoria, CancellationToken ct = default);
    Task<IReadOnlyList<Auditoria>> ListarPorInstitucionAsync(Guid institucionId, int limite = 50, CancellationToken ct = default);
    Task<IReadOnlyList<Auditoria>> ListarPorEntidadAsync(string entidad, Guid institucionId, int limite = 50, CancellationToken ct = default);
}
