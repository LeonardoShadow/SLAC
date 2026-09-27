using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Institucional.Entities;
using SLAC.Core.Institucional.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseDiaNoLectivoRepository : IDiaNoLectivoRepository
{
    private readonly Client _supabaseClient;
    private readonly ILogger<SupabaseDiaNoLectivoRepository> _logger;
    private static readonly ConcurrentDictionary<Guid, DiaNoLectivo> _fallbackStore = new();

    public SupabaseDiaNoLectivoRepository(Client supabaseClient, ILogger<SupabaseDiaNoLectivoRepository> logger)
    {
        _supabaseClient = supabaseClient;
        _logger = logger;
    }

    public async Task<List<DiaNoLectivo>> GetByInstitucionAsync(Guid institucionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<DiaNoLectivoDbModel>()
                .Where(x => x.InstitucionId == institucionId)
                .Get(cancellationToken);

            if (response?.Models?.Count > 0)
            {
                return response.Models.Select(m => new DiaNoLectivo
                {
                    Id = m.Id,
                    InstitucionId = m.InstitucionId,
                    Fecha = DateOnly.TryParse(m.Fecha, out var f) ? f : DateOnly.MinValue,
                    Motivo = m.Motivo,
                    CreadoEn = m.CreadoEn
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al consultar días no lectivos en Supabase. Usando almacén local.");
        }

        return _fallbackStore.Values.Where(d => d.InstitucionId == institucionId).ToList();
    }

    public async Task<bool> IsDiaNoLectivoAsync(Guid institucionId, DateOnly fecha, CancellationToken cancellationToken = default)
    {
        var fechaStr = fecha.ToString("yyyy-MM-dd");

        try
        {
            var response = await _supabaseClient
                .From<DiaNoLectivoDbModel>()
                .Where(x => x.InstitucionId == institucionId && x.Fecha == fechaStr)
                .Get(cancellationToken);

            if (response?.Models?.Count > 0)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al verificar día no lectivo en Supabase.");
        }

        return _fallbackStore.Values.Any(d => d.InstitucionId == institucionId && d.Fecha == fecha);
    }

    public async Task<DiaNoLectivo> CreateAsync(DiaNoLectivo diaNoLectivo, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbModel = new DiaNoLectivoDbModel
            {
                Id = diaNoLectivo.Id == Guid.Empty ? Guid.NewGuid() : diaNoLectivo.Id,
                InstitucionId = diaNoLectivo.InstitucionId,
                Fecha = diaNoLectivo.Fecha.ToString("yyyy-MM-dd"),
                Motivo = diaNoLectivo.Motivo,
                CreadoEn = diaNoLectivo.CreadoEn.UtcDateTime
            };

            await _supabaseClient.From<DiaNoLectivoDbModel>().Insert(dbModel, null, cancellationToken);
            diaNoLectivo.Id = dbModel.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al insertar día no lectivo en Supabase. Guardando localmente.");
            if (diaNoLectivo.Id == Guid.Empty) diaNoLectivo.Id = Guid.NewGuid();
        }

        _fallbackStore[diaNoLectivo.Id] = diaNoLectivo;
        return diaNoLectivo;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _supabaseClient.From<DiaNoLectivoDbModel>().Where(x => x.Id == id).Delete(null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al eliminar día no lectivo {Id} en Supabase.", id);
        }

        _fallbackStore.TryRemove(id, out _);
    }
}
