using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Institucional.Entities;
using SLAC.Core.Institucional.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseInstitucionRepository(Client supabaseClient, ILogger<SupabaseInstitucionRepository> logger) : IInstitucionRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseInstitucionRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, Institucion> _fallbackStore = new();

    static SupabaseInstitucionRepository()
    {
        // Tenant inicial por defecto: UPDS
        var defaultId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _fallbackStore[defaultId] = new Institucion
        {
            Id = defaultId,
            Nombre = "Universidad Privada Domingo Savio (UPDS)",
            Tipo = "universidad",
            ZonaHoraria = "America/La_Paz",
            VentanaMin = 20,
            RotacionSeg = 15,
            Estado = "activo",
            CreadoEn = DateTimeOffset.UtcNow,
            ActualizadoEn = DateTimeOffset.UtcNow
        };
    }

    public async Task<IReadOnlyList<Institucion>> ListarTodasAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<InstitucionDbModel>()
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                var list = response.Models.ConvertAll(MapToEntity);
                foreach (var item in list)
                {
                    _fallbackStore[item.Id] = item;
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al listar instituciones desde Supabase. Usando almacén local.");
        }

        return [.. _fallbackStore.Values.OrderBy(i => i.Nombre)];
    }

    public async Task<Institucion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<InstitucionDbModel>()
                .Where(x => x.Id == id)
                .Single(ct);

            if (response != null)
            {
                var entity = MapToEntity(response);
                _fallbackStore[id] = entity;
                return entity;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al obtener institución {Id} desde Supabase.", id);
        }

        return _fallbackStore.TryGetValue(id, out var inst) ? inst : null;
    }

    public async Task<Institucion> GuardarAsync(Institucion institucion, CancellationToken ct = default)
    {
        if (institucion.Id == Guid.Empty)
        {
            institucion.Id = Guid.NewGuid();
        }

        institucion.ActualizadoEn = DateTimeOffset.UtcNow;
        _fallbackStore[institucion.Id] = institucion;

        try
        {
            var model = new InstitucionDbModel
            {
                Id = institucion.Id,
                Nombre = institucion.Nombre,
                Tipo = institucion.Tipo,
                ZonaHoraria = institucion.ZonaHoraria,
                VentanaMin = institucion.VentanaMin,
                RotacionSeg = institucion.RotacionSeg,
                Estado = institucion.Estado,
                CreadoEn = institucion.CreadoEn.UtcDateTime,
                ActualizadoEn = institucion.ActualizadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<InstitucionDbModel>()
                .Upsert(model, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al guardar institución {Id} en Supabase.", institucion.Id);
        }

        return institucion;
    }

    public async Task<bool> CambiarEstadoAsync(Guid id, string nuevoEstado, CancellationToken ct = default)
    {
        var inst = await ObtenerPorIdAsync(id, ct);
        if (inst == null) return false;

        inst.Estado = nuevoEstado;
        inst.ActualizadoEn = DateTimeOffset.UtcNow;
        _fallbackStore[id] = inst;

        try
        {
            await _supabaseClient
                .From<InstitucionDbModel>()
                .Where(x => x.Id == id)
                .Set(x => x.Estado, nuevoEstado)
                .Set(x => x.ActualizadoEn, DateTime.UtcNow)
                .Update(cancellationToken: ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al actualizar estado de institución {Id} en Supabase.", id);
            return true; // Se mantiene en memoria local
        }
    }

    private static Institucion MapToEntity(InstitucionDbModel m) => new()
    {
        Id = m.Id,
        Nombre = m.Nombre,
        Tipo = m.Tipo,
        ZonaHoraria = m.ZonaHoraria,
        VentanaMin = m.VentanaMin,
        RotacionSeg = m.RotacionSeg,
        Estado = m.Estado,
        CreadoEn = m.CreadoEn,
        ActualizadoEn = m.ActualizadoEn
    };
}
