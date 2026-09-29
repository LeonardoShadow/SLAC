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
        var resultados = new Dictionary<Guid, Revinculacion>();

        try
        {
            var response = await _supabaseClient
                .From<RevinculacionDbModel>()
                .Filter("materia_id", Postgrest.Constants.Operator.Equals, materiaId.ToString())
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                foreach (var model in response.Models)
                {
                    var entity = MapToEntity(model);
                    if (entity.EstaPendiente)
                    {
                        resultados[entity.Id] = entity;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando revinculaciones en Supabase. Usando almacén local.");
        }

        // Siempre fusionar con las solicitudes locales del almacén en memoria
        foreach (var local in _fallbackStore.Values)
        {
            if (local.MateriaId == materiaId && local.EstaPendiente)
            {
                resultados[local.Id] = local;
            }
        }

        return [.. resultados.Values.OrderByDescending(r => r.CreadoEn)];
    }

    public async Task<Revinculacion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        if (_fallbackStore.TryGetValue(id, out var local))
        {
            return local;
        }

        try
        {
            var response = await _supabaseClient
                .From<RevinculacionDbModel>()
                .Filter("id", Postgrest.Constants.Operator.Equals, id.ToString())
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

        return null;
    }

    public async Task<Revinculacion?> ObtenerPendientePorEstudianteYMateriaAsync(Guid estudianteId, Guid materiaId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<RevinculacionDbModel>()
                .Filter("estudiante_id", Postgrest.Constants.Operator.Equals, estudianteId.ToString())
                .Filter("materia_id", Postgrest.Constants.Operator.Equals, materiaId.ToString())
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                var pending = response.Models
                    .Select(MapToEntity)
                    .FirstOrDefault(r => r.EstaPendiente);

                if (pending != null)
                {
                    return pending;
                }
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
        var now = DateTimeOffset.UtcNow;
        if (_fallbackStore.TryGetValue(id, out var r))
        {
            r.UsadaEn = now;
        }

        try
        {
            await _supabaseClient
                .From<RevinculacionDbModel>()
                .Filter("id", Postgrest.Constants.Operator.Equals, id.ToString())
                .Set(x => x.UsadaEn!, now.UtcDateTime)
                .Update(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error actualizando revinculación {Id} como usada en Supabase.", id);
        }
    }

    private static Revinculacion MapToEntity(RevinculacionDbModel m)
    {
        var expiraUtc = m.ExpiraEn.Kind switch
        {
            DateTimeKind.Utc => m.ExpiraEn,
            DateTimeKind.Local => m.ExpiraEn.ToUniversalTime(),
            _ => DateTime.SpecifyKind(m.ExpiraEn, DateTimeKind.Utc)
        };

        var creadaUtc = m.CreadoEn.Kind switch
        {
            DateTimeKind.Utc => m.CreadoEn,
            DateTimeKind.Local => m.CreadoEn.ToUniversalTime(),
            _ => DateTime.SpecifyKind(m.CreadoEn, DateTimeKind.Utc)
        };

        DateTimeOffset? usadaOffset = null;
        if (m.UsadaEn.HasValue)
        {
            var uUtc = m.UsadaEn.Value.Kind switch
            {
                DateTimeKind.Utc => m.UsadaEn.Value,
                DateTimeKind.Local => m.UsadaEn.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(m.UsadaEn.Value, DateTimeKind.Utc)
            };
            usadaOffset = new DateTimeOffset(uUtc, TimeSpan.Zero);
        }

        return new Revinculacion
        {
            Id = m.Id,
            InstitucionId = m.InstitucionId,
            EstudianteId = m.EstudianteId,
            MateriaId = m.MateriaId,
            DocenteId = m.DocenteId,
            ExpiraEn = new DateTimeOffset(expiraUtc, TimeSpan.Zero),
            UsadaEn = usadaOffset,
            CreadoEn = new DateTimeOffset(creadaUtc, TimeSpan.Zero)
        };
    }
}
