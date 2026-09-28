using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseRevinculacionRepository(Client supabaseClient, ILogger<SupabaseRevinculacionRepository> logger) : IRevinculacionRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseRevinculacionRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, Revinculacion> _fallbackStore = new();

    public async Task<Revinculacion> CrearSolicitudAsync(Revinculacion solicitud, CancellationToken ct = default)
    {
        if (solicitud.Id == Guid.Empty)
        {
            solicitud.Id = Guid.NewGuid();
        }

        solicitud.CreadoEn = DateTimeOffset.UtcNow;
        _fallbackStore[solicitud.Id] = solicitud;

        try
        {
            var model = new RevinculacionDbModel
            {
                Id = solicitud.Id,
                InstitucionId = solicitud.InstitucionId,
                EstudianteId = solicitud.EstudianteId,
                MateriaId = solicitud.MateriaId,
                DocenteId = solicitud.DocenteId,
                ExpiraEn = solicitud.ExpiraEn.UtcDateTime,
                UsadaEn = solicitud.UsadaEn?.UtcDateTime,
                CreadoEn = solicitud.CreadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<RevinculacionDbModel>()
                .Insert(model, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error creando solicitud de revinculación en Supabase. Se mantiene en memoria local.");
        }

        return solicitud;
    }

    public async Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        try
        {
            var response = await _supabaseClient
                .From<RevinculacionDbModel>()
                .Where(x => x.MateriaId == materiaId)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models
                    .Where(m => m.UsadaEn == null && m.ExpiraEn > ahora)
                    .Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando revinculaciones en Supabase. Usando almacén local.");
        }

        return [.. _fallbackStore.Values
            .Where(r => r.MateriaId == materiaId && r.EstaPendiente)];
    }

    public async Task<Revinculacion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<RevinculacionDbModel>()
                .Where(x => x.Id == id)
                .Single(ct);

            if (response != null)
            {
                return MapToEntity(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando revinculación {Id} en Supabase.", id);
        }

        return _fallbackStore.TryGetValue(id, out var rev) ? rev : null;
    }

    public async Task<Revinculacion?> ObtenerPendientePorEstudianteYMateriaAsync(Guid estudianteId, Guid materiaId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        try
        {
            var response = await _supabaseClient
                .From<RevinculacionDbModel>()
                .Where(x => x.EstudianteId == estudianteId && x.MateriaId == materiaId)
                .Get(ct);

            var pending = response?.Models?.FirstOrDefault(m => m.UsadaEn == null && m.ExpiraEn > ahora);
            if (pending != null)
            {
                return MapToEntity(pending);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando revinculación pendiente de estudiante en Supabase.");
        }

        return _fallbackStore.Values.FirstOrDefault(r =>
            r.EstudianteId == estudianteId &&
            r.MateriaId == materiaId &&
            r.EstaPendiente);
    }

    public async Task MarcarComoUsadaAsync(Guid id, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (_fallbackStore.TryGetValue(id, out var r))
        {
            r.UsadaEn = new DateTimeOffset(now, TimeSpan.Zero);
        }

        try
        {
            await _supabaseClient
                .From<RevinculacionDbModel>()
                .Where(x => x.Id == id)
                .Set(x => x.UsadaEn!, now)
                .Update(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error actualizando revinculación {Id} como usada en Supabase.", id);
        }
    }

    private static Revinculacion MapToEntity(RevinculacionDbModel m) => new()
    {
        Id = m.Id,
        InstitucionId = m.InstitucionId,
        EstudianteId = m.EstudianteId,
        MateriaId = m.MateriaId,
        DocenteId = m.DocenteId,
        ExpiraEn = m.ExpiraEn,
        UsadaEn = m.UsadaEn,
        CreadoEn = m.CreadoEn
    };
}
