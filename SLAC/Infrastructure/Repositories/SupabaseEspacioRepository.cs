using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Institucional.Entities;
using SLAC.Core.Institucional.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseEspacioRepository : IEspacioRepository
{
    private readonly Client _supabaseClient;
    private readonly ILogger<SupabaseEspacioRepository> _logger;

    // Respaldo en memoria para pruebas y desarrollo
    private static readonly ConcurrentDictionary<Guid, Espacio> _fallbackStore = new();

    public SupabaseEspacioRepository(Client supabaseClient, ILogger<SupabaseEspacioRepository> logger)
    {
        _supabaseClient = supabaseClient;
        _logger = logger;
    }

    public async Task<List<Espacio>> GetByInstitucionAsync(Guid institucionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<EspacioDbModel>()
                .Where(x => x.InstitucionId == institucionId)
                .Get(cancellationToken);

            if (response?.Models?.Count > 0)
            {
                return response.Models.Select(m => new Espacio
                {
                    Id = m.Id,
                    InstitucionId = m.InstitucionId,
                    Nombre = m.Nombre,
                    Tipo = m.Tipo,
                    Capacidad = m.Capacidad,
                    CreadoEn = m.CreadoEn
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al consultar espacios en Supabase. Usando almacén local.");
        }

        return _fallbackStore.Values.Where(e => e.InstitucionId == institucionId).ToList();
    }

    public async Task<Espacio?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<EspacioDbModel>()
                .Where(x => x.Id == id)
                .Single(cancellationToken);

            if (response != null)
            {
                return new Espacio
                {
                    Id = response.Id,
                    InstitucionId = response.InstitucionId,
                    Nombre = response.Nombre,
                    Tipo = response.Tipo,
                    Capacidad = response.Capacidad,
                    CreadoEn = response.CreadoEn
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al consultar espacio {Id} en Supabase.", id);
        }

        _fallbackStore.TryGetValue(id, out var espacio);
        return espacio;
    }

    public async Task<Espacio> CreateAsync(Espacio espacio, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbModel = new EspacioDbModel
            {
                Id = espacio.Id == Guid.Empty ? Guid.NewGuid() : espacio.Id,
                InstitucionId = espacio.InstitucionId,
                Nombre = espacio.Nombre,
                Tipo = espacio.Tipo,
                Capacidad = espacio.Capacidad,
                CreadoEn = espacio.CreadoEn.UtcDateTime
            };

            await _supabaseClient.From<EspacioDbModel>().Insert(dbModel, null, cancellationToken);
            espacio.Id = dbModel.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al insertar espacio en Supabase. Guardando localmente.");
            if (espacio.Id == Guid.Empty) espacio.Id = Guid.NewGuid();
        }

        _fallbackStore[espacio.Id] = espacio;
        return espacio;
    }

    public async Task<Espacio> UpdateAsync(Espacio espacio, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbModel = new EspacioDbModel
            {
                Id = espacio.Id,
                InstitucionId = espacio.InstitucionId,
                Nombre = espacio.Nombre,
                Tipo = espacio.Tipo,
                Capacidad = espacio.Capacidad,
                CreadoEn = espacio.CreadoEn.UtcDateTime
            };

            await _supabaseClient.From<EspacioDbModel>().Update(dbModel, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al actualizar espacio {Id} en Supabase.", espacio.Id);
        }

        _fallbackStore[espacio.Id] = espacio;
        return espacio;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _supabaseClient.From<EspacioDbModel>().Where(x => x.Id == id).Delete(null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al eliminar espacio {Id} en Supabase.", id);
        }

        _fallbackStore.TryRemove(id, out _);
    }
}
