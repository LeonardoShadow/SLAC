using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseAsistenciaDetalleRepository(Client supabaseClient, ILogger<SupabaseAsistenciaDetalleRepository> logger) : IAsistenciaDetalleRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseAsistenciaDetalleRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, AsistenciaDetalle> _fallbackStore = new();

    public async Task<IReadOnlyList<AsistenciaDetalle>> ListarPorListaAsync(Guid listaId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<AsistenciaDetalleDbModel>()
                .Where(x => x.ListaId == listaId)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models.Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando asistencia_detalle para lista {ListaId} en Supabase.", listaId);
        }

        return [.. _fallbackStore.Values.Where(d => d.ListaId == listaId)];
    }

    public async Task<AsistenciaDetalle?> ObtenerPorListaYEstudianteAsync(Guid listaId, Guid estudianteId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<AsistenciaDetalleDbModel>()
                .Where(x => x.ListaId == listaId && x.EstudianteId == estudianteId)
                .Single(ct);

            if (response != null)
            {
                return MapToEntity(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando asistencia de estudiante {EstudianteId} en lista {ListaId}.", estudianteId, listaId);
        }

        return _fallbackStore.Values.FirstOrDefault(d => d.ListaId == listaId && d.EstudianteId == estudianteId);
    }

    public async Task<AsistenciaDetalle> RegistrarAsistenciaAsync(AsistenciaDetalle detalle, CancellationToken ct = default)
    {
        if (detalle.Id == Guid.Empty)
        {
            detalle.Id = Guid.NewGuid();
        }

        _fallbackStore[detalle.Id] = detalle;

        try
        {
            var model = new AsistenciaDetalleDbModel
            {
                Id = detalle.Id,
                InstitucionId = detalle.InstitucionId,
                ListaId = detalle.ListaId,
                EstudianteId = detalle.EstudianteId,
                Estado = detalle.Estado,
                Origen = detalle.Origen,
                HoraLlegada = detalle.HoraLlegada?.UtcDateTime,
                MinutosDesdeInicio = detalle.MinutosDesdeInicio,
                DispositivoId = detalle.DispositivoId,
                Latitud = detalle.Latitud,
                Longitud = detalle.Longitud,
                PrecisionGps = detalle.PrecisionGps,
                CreadoEn = detalle.CreadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<AsistenciaDetalleDbModel>()
                .Upsert(model, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error registrando asistencia_detalle en Supabase. Se mantiene en almacén local.");
        }

        return detalle;
    }

    public async Task<int> RegistrarFaltasIdempotenteAsync(Guid listaId, Guid institucionId, IEnumerable<Guid> estudiantesIds, CancellationToken ct = default)
    {
        var count = 0;
        var now = DateTimeOffset.UtcNow;

        // 1. Cargar estudiantes que ya tienen registro previo en esta lista (Presente o Falta)
        var registrados = new HashSet<Guid>(_fallbackStore.Values.Where(d => d.ListaId == listaId).Select(d => d.EstudianteId));
        try
        {
            var resDb = await _supabaseClient
                .From<AsistenciaDetalleDbModel>()
                .Filter("lista_id", Postgrest.Constants.Operator.Equals, listaId.ToString())
                .Get(ct);

            if (resDb?.Models?.Count > 0)
            {
                foreach (var m in resDb.Models)
                {
                    registrados.Add(m.EstudianteId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Consulta previa de asistencias para lista {ListaId} omitida o fallida. Se validará individualmente.", listaId);
        }

        // 2. Insertar falta únicamente para los estudiantes que no tengan ningún registro previo
        foreach (var estudianteId in estudiantesIds)
        {
            if (registrados.Contains(estudianteId))
            {
                continue;
            }

            var falta = new AsistenciaDetalle
            {
                Id = Guid.NewGuid(),
                InstitucionId = institucionId,
                ListaId = listaId,
                EstudianteId = estudianteId,
                Estado = "Falta",
                Origen = "Cierre",
                CreadoEn = now
            };

            _fallbackStore[falta.Id] = falta;
            registrados.Add(estudianteId);
            count++;

            try
            {
                var model = new AsistenciaDetalleDbModel
                {
                    Id = falta.Id,
                    InstitucionId = falta.InstitucionId,
                    ListaId = falta.ListaId,
                    EstudianteId = falta.EstudianteId,
                    Estado = falta.Estado,
                    Origen = falta.Origen,
                    CreadoEn = falta.CreadoEn.UtcDateTime
                };

                await _supabaseClient
                    .From<AsistenciaDetalleDbModel>()
                    .Insert(model, null, ct);
            }
            catch (Postgrest.Exceptions.PostgrestException pex) when (pex.Message?.Contains("uq_lista_estudiante") is true || pex.Message?.Contains("23505") is true)
            {
                // Es un caso normal de idempotencia: el estudiante ya tenía un registro en BD.
                _logger.LogDebug(pex, "Estudiante {EstudianteId} ya tenía registro en la lista {ListaId}. Se mantiene el estado previo.", estudianteId, listaId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error insertando falta automática en Supabase para estudiante {EstudianteId}.", estudianteId);
            }
        }

        return count;
    }

    public async Task EliminarPorListaAsync(Guid listaId, CancellationToken ct = default)
    {
        var idsToRemove = _fallbackStore.Values.Where(d => d.ListaId == listaId).Select(d => d.Id).ToList();
        foreach (var id in idsToRemove)
        {
            _fallbackStore.TryRemove(id, out _);
        }

        try
        {
            await _supabaseClient
                .From<AsistenciaDetalleDbModel>()
                .Filter("lista_id", Postgrest.Constants.Operator.Equals, listaId.ToString())
                .Delete(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error eliminando detalles de asistencia para lista {ListaId} en Supabase.", listaId);
        }
    }

    private static AsistenciaDetalle MapToEntity(AsistenciaDetalleDbModel m) => new()
    {
        Id = m.Id,
        InstitucionId = m.InstitucionId,
        ListaId = m.ListaId,
        EstudianteId = m.EstudianteId,
        Estado = m.Estado,
        Origen = m.Origen,
        HoraLlegada = m.HoraLlegada.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(m.HoraLlegada.Value, DateTimeKind.Utc))
            : null,
        MinutosDesdeInicio = m.MinutosDesdeInicio,
        DispositivoId = m.DispositivoId,
        Latitud = m.Latitud,
        Longitud = m.Longitud,
        PrecisionGps = m.PrecisionGps,
        CreadoEn = new DateTimeOffset(DateTime.SpecifyKind(m.CreadoEn, DateTimeKind.Utc))
    };
}
