using System;

namespace SLAC.Core;

/// <summary>
/// Proveedor de hora y fecha unificado para SLAC.
/// Garantiza que en cualquier entorno de hosting (Azure, AWS, RunASP en UTC)
/// la fecha y hora siempre correspondan a la zona horaria institucional (Bolivia UTC-4).
/// </summary>
public static class TimeHelper
{
    private static readonly TimeZoneInfo BoliviaTimeZone = ObtenerZonaHorariaBolivia();
    public static readonly TimeSpan BoliviaOffset = TimeSpan.FromHours(-4);

    private static TimeZoneInfo ObtenerZonaHorariaBolivia()
    {
        try
        {
            // Windows ID
            return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time");
        }
        catch
        {
            try
            {
                // Linux / IANA ID
                return TimeZoneInfo.FindSystemTimeZoneById("America/La_Paz");
            }
            catch
            {
                // Fallback directo a UTC-4
                return TimeZoneInfo.CreateCustomTimeZone("BOT", TimeSpan.FromHours(-4), "Bolivia Time", "Bolivia Time");
            }
        }
    }

    /// <summary>
    /// Fecha y hora actual en la zona institucional (UTC-4).
    /// </summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaTimeZone);

    /// <summary>
    /// Fecha actual institucional (DateOnly).
    /// </summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>
    /// Hora actual institucional (TimeSpan desde las 00:00:00).
    /// </summary>
    public static TimeSpan TimeOfDay => Now.TimeOfDay;

    /// <summary>
    /// Convierte cualquier DateTimeOffset o UTC a la hora local boliviana formateada en HH:mm:ss.
    /// </summary>
    public static string FormatearHoraLocal(DateTimeOffset? fechaUtc)
    {
        if (!fechaUtc.HasValue) return Now.ToString("HH:mm:ss");
        var local = TimeZoneInfo.ConvertTime(fechaUtc.Value, BoliviaTimeZone);
        return local.ToString("HH:mm:ss");
    }

    /// <summary>
    /// Convierte un DateTime UTC a fecha y hora local boliviana (dd/MM/yyyy HH:mm).
    /// </summary>
    public static string FormatearFechaHoraLocal(DateTime? fechaUtc)
    {
        if (!fechaUtc.HasValue) return Now.ToString("dd/MM/yyyy HH:mm");
        var utc = DateTime.SpecifyKind(fechaUtc.Value, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, BoliviaTimeZone);
        return local.ToString("dd/MM/yyyy HH:mm");
    }

    /// <summary>
    /// Convierte un DateTimeOffset a fecha y hora local boliviana (dd/MM/yyyy HH:mm).
    /// </summary>
    public static string FormatearFechaHoraLocal(DateTimeOffset? fechaUtc)
    {
        if (!fechaUtc.HasValue) return Now.ToString("dd/MM/yyyy HH:mm");
        var local = TimeZoneInfo.ConvertTime(fechaUtc.Value, BoliviaTimeZone);
        return local.ToString("dd/MM/yyyy HH:mm");
    }

    /// <summary>
    /// Convierte un DateTime a formato de fecha legible en Bolivia (ej. Miércoles, 30 de Septiembre de 2026).
    /// </summary>
    public static string FormatearFechaLarga(DateOnly fecha)
    {
        var dt = fecha.ToDateTime(TimeOnly.MinValue);
        var ci = new System.Globalization.CultureInfo("es-BO");
        return dt.ToString("dddd, dd 'de' MMMM 'de' yyyy", ci);
    }
}
