using SLAC.Core.Institucional.Entities;

namespace SLAC.Core.Institucional.Services;

/// <summary>
/// Abstracción para resolución de tiempo y zona horaria según la configuración multi-tenant (REF-10).
/// </summary>
public interface IInstitutionalTimeProvider
{
    DateTimeOffset GetUtcNow();
    DateTime GetLocalNow(Institucion institucion);
    DateTime GetLocalNow(string? timeZoneId);
    DateOnly GetLocalDate(Institucion institucion);
}
