using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Docentes.Entities;
using SLAC.Core.Docentes.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseDocenteRepository(Client supabaseClient, ILogger<SupabaseDocenteRepository> logger) : IDocenteRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseDocenteRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, Docente> _fallbackStore = new();

    public async Task<IReadOnlyList<Docente>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<DocenteDbModel>()
                .Where(x => x.InstitucionId == institucionId)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models.Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando docentes en Supabase. Usando almacén local.");
        }

        return [.. _fallbackStore.Values.Where(d => d.InstitucionId == institucionId)];
    }

    public async Task<Docente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<DocenteDbModel>()
                .Where(x => x.Id == id)
                .Single(ct);

            if (response != null)
            {
                return MapToEntity(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando docente {Id} en Supabase. Usando almacén local.", id);
        }

        return _fallbackStore.TryGetValue(id, out var doc) ? doc : null;
    }

    public async Task<Docente?> ObtenerPorUsuarioIdAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<DocenteDbModel>()
                .Where(x => x.UserId == userId)
                .Single(ct);

            if (response != null)
            {
                return MapToEntity(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando docente por userId {UserId} en Supabase. Usando almacén local.", userId);
        }

        return _fallbackStore.Values.FirstOrDefault(d => d.UserId == userId);
    }

    public async Task<Docente> GuardarAsync(Docente docente, CancellationToken ct = default)
    {
        if (docente.Id == Guid.Empty)
        {
            docente.Id = Guid.NewGuid();
        }

        _fallbackStore[docente.Id] = docente;

        try
        {
            var model = new DocenteDbModel
            {
                Id = docente.Id,
                InstitucionId = docente.InstitucionId,
                UserId = docente.UserId,
                Nombres = docente.Nombres,
                Apellidos = docente.Apellidos,
                Correo = docente.Correo,
                CreadoEn = docente.CreadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<DocenteDbModel>()
                .Upsert(model, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error guardando docente en Supabase. Se mantiene en almacén local.");
        }

        return docente;
    }

    private static Docente MapToEntity(DocenteDbModel m) => new()
    {
        Id = m.Id,
        InstitucionId = m.InstitucionId,
        UserId = m.UserId,
        Nombres = m.Nombres,
        Apellidos = m.Apellidos,
        Correo = m.Correo,
        CreadoEn = m.CreadoEn
    };
}
