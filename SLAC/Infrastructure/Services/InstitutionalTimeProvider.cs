using SLAC.Core.Institucional.Entities;
using SLAC.Core.Institucional.Services;

namespace SLAC.Infrastructure.Services;

/// <summary>
/// Implementación de IInstitutionalTimeProvider basada en TimeProvider estándar de .NET 9 (REF-10).
/// </summary>
public class InstitutionalTimeProvider(TimeProvider timeProvider) : IInstitutionalTimeProvider
{
    private readonly TimeProvider _timeProvider = timeProvider;

    public DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();

    public DateTime GetLocalNow(Institucion institucion) => GetLocalNow(institucion.ZonaHoraria);

    public DateTime GetLocalNow(string? timeZoneId)
    {
        var tzId = string.IsNullOrWhiteSpace(timeZoneId) ? "SA Western Standard Time" : timeZoneId;
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch
        {
            try
            {
                // Intento con ID de fallback para Bolivia / Linux
                tz = TimeZoneInfo.FindSystemTimeZoneById(tzId == "SA Western Standard Time" ? "America/La_Paz" : "SA Western Standard Time");
            }
            catch
            {
                tz = TimeZoneInfo.CreateCustomTimeZone("BOT", TimeSpan.FromHours(-4), "Bolivia Time", "Bolivia Time");
            }
        }

        return TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, tz);
    }

    public DateOnly GetLocalDate(Institucion institucion)
    {
        var localNow = GetLocalNow(institucion);
        return DateOnly.FromDateTime(localNow);
    }
}
