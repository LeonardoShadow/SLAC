using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Institucional.Entities;
using SLAC.Core.Institucional.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabasePeriodoRepository : IPeriodoAcademicoRepository
{
    private readonly Client _supabaseClient;
    private readonly ILogger<SupabasePeriodoRepository> _logger;
    private static readonly ConcurrentDictionary<Guid, PeriodoAcademico> _fallbackStore = new();

    public SupabasePeriodoRepository(Client supabaseClient, ILogger<SupabasePeriodoRepository> logger)
    {
        _supabaseClient = supabaseClient;
        _logger = logger;
    }

    public async Task<List<PeriodoAcademico>> GetByInstitucionAsync(Guid institucionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<PeriodoDbModel>()
                .Where(x => x.InstitucionId == institucionId)
                .Get(cancellationToken);

            if (response?.Models?.Count > 0)
            {
                return response.Models.Select(m => new PeriodoAcademico
                {
                    Id = m.Id,
                    InstitucionId = m.InstitucionId,
                    Nombre = m.Nombre,
                    FechaInicio = DateOnly.TryParse(m.FechaInicio, out var fi) ? fi : DateOnly.MinValue,
                    FechaFin = DateOnly.TryParse(m.FechaFin, out var ff) ? ff : DateOnly.MinValue,
                    CreadoEn = m.CreadoEn
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al consultar periodos en Supabase. Usando almacén local.");
        }

        return _fallbackStore.Values.Where(p => p.InstitucionId == institucionId).ToList();
    }

    public async Task<PeriodoAcademico?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<PeriodoDbModel>()
                .Where(x => x.Id == id)
                .Single(cancellationToken);

            if (response != null)
            {
                return new PeriodoAcademico
                {
                    Id = response.Id,
                    InstitucionId = response.InstitucionId,
                    Nombre = response.Nombre,
                    FechaInicio = DateOnly.TryParse(response.FechaInicio, out var fi) ? fi : DateOnly.MinValue,
                    FechaFin = DateOnly.TryParse(response.FechaFin, out var ff) ? ff : DateOnly.MinValue,
                    CreadoEn = response.CreadoEn
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al consultar periodo {Id} en Supabase.", id);
        }

        _fallbackStore.TryGetValue(id, out var p);
        return p;
    }

    public async Task<PeriodoAcademico> CreateAsync(PeriodoAcademico periodo, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbModel = new PeriodoDbModel
            {
                Id = periodo.Id == Guid.Empty ? Guid.NewGuid() : periodo.Id,
                InstitucionId = periodo.InstitucionId,
                Nombre = periodo.Nombre,
                FechaInicio = periodo.FechaInicio.ToString("yyyy-MM-dd"),
                FechaFin = periodo.FechaFin.ToString("yyyy-MM-dd"),
                CreadoEn = periodo.CreadoEn.UtcDateTime
            };

            await _supabaseClient.From<PeriodoDbModel>().Insert(dbModel, null, cancellationToken);
            periodo.Id = dbModel.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al insertar periodo en Supabase. Guardando localmente.");
            if (periodo.Id == Guid.Empty) periodo.Id = Guid.NewGuid();
        }

        _fallbackStore[periodo.Id] = periodo;
        return periodo;
    }

    public async Task<PeriodoAcademico> UpdateAsync(PeriodoAcademico periodo, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbModel = new PeriodoDbModel
            {
                Id = periodo.Id,
                InstitucionId = periodo.InstitucionId,
                Nombre = periodo.Nombre,
                FechaInicio = periodo.FechaInicio.ToString("yyyy-MM-dd"),
                FechaFin = periodo.FechaFin.ToString("yyyy-MM-dd"),
                CreadoEn = periodo.CreadoEn.UtcDateTime
            };

            await _supabaseClient.From<PeriodoDbModel>().Update(dbModel, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al actualizar periodo {Id} en Supabase.", periodo.Id);
        }

        _fallbackStore[periodo.Id] = periodo;
        return periodo;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _supabaseClient.From<PeriodoDbModel>().Where(x => x.Id == id).Delete(null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al eliminar periodo {Id} en Supabase.", id);
        }

        _fallbackStore.TryRemove(id, out _);
    }
}
