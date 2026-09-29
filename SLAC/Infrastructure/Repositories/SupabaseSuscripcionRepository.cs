using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseSuscripcionRepository(Client supabaseClient, ILogger<SupabaseSuscripcionRepository> logger) : ISuscripcionRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseSuscripcionRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, Suscripcion> _fallbackStore = new();

    public async Task<IReadOnlyList<Suscripcion>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<SuscripcionDbModel>()
                .Where(x => x.MateriaId == materiaId && x.Estado == "activa")
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models.Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando suscripciones para materia {MateriaId} en Supabase.", materiaId);
        }

        return [.. _fallbackStore.Values.Where(s => s.MateriaId == materiaId && s.Estado == "activa")];
    }

    public async Task<IReadOnlyList<Guid>> ListarEstudiantesIdsPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
    {
        var suscripciones = await ListarPorMateriaAsync(materiaId, ct);
        return [.. suscripciones.Select(s => s.EstudianteId)];
    }

    public async Task<Suscripcion?> ObtenerAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<SuscripcionDbModel>()
                .Where(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId)
                .Single(ct);

            if (response != null)
            {
                return MapToEntity(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando suscripción para estudiante {EstudianteId} en materia {MateriaId}.", estudianteId, materiaId);
        }

        return _fallbackStore.Values.FirstOrDefault(s => s.MateriaId == materiaId && s.EstudianteId == estudianteId);
    }

    public async Task<Suscripcion> SuscribirAsync(Suscripcion suscripcion, CancellationToken ct = default)
    {
        if (suscripcion.Id == Guid.Empty)
        {
            suscripcion.Id = Guid.NewGuid();
        }

        _fallbackStore[suscripcion.Id] = suscripcion;

        try
        {
            var model = new SuscripcionDbModel
            {
                Id = suscripcion.Id,
                InstitucionId = suscripcion.InstitucionId,
                MateriaId = suscripcion.MateriaId,
                EstudianteId = suscripcion.EstudianteId,
                Fecha = suscripcion.Fecha.UtcDateTime,
                ListaOrigenId = suscripcion.ListaOrigenId,
                Estado = suscripcion.Estado,
                CreadoEn = suscripcion.CreadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<SuscripcionDbModel>()
                .Upsert(model, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error guardando suscripción en Supabase. Se mantiene en almacén local.");
        }

        return suscripcion;
    }

    public async Task<bool> DesinscribirAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default)
    {
        try
        {
            await _supabaseClient
                .From<SuscripcionDbModel>()
                .Where(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId)
                .Delete(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al desinscribir estudiante {EstudianteId} de materia {MateriaId} en Supabase.", estudianteId, materiaId);
        }

        var match = _fallbackStore.FirstOrDefault(x => x.Value.MateriaId == materiaId && x.Value.EstudianteId == estudianteId);
        if (match.Key != Guid.Empty)
        {
            _fallbackStore.TryRemove(match.Key, out _);
        }

        return true;
    }

    private static Suscripcion MapToEntity(SuscripcionDbModel m) => new()
    {
        Id = m.Id,
        InstitucionId = m.InstitucionId,
        MateriaId = m.MateriaId,
        EstudianteId = m.EstudianteId,
        Fecha = m.Fecha,
        ListaOrigenId = m.ListaOrigenId,
        Estado = m.Estado,
        CreadoEn = m.CreadoEn
    };
}
