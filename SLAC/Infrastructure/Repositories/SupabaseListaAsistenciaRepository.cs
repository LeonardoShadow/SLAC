using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseListaAsistenciaRepository(Client supabaseClient, ILogger<SupabaseListaAsistenciaRepository> logger) : IListaAsistenciaRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseListaAsistenciaRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, ListaAsistencia> _fallbackStore = new();

    public async Task<ListaAsistencia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<ListaAsistenciaDbModel>()
                .Where(x => x.Id == id)
                .Single(ct);

            if (response != null)
            {
                return MapToEntity(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando lista_asistencia {Id} en Supabase. Usando almacén local.", id);
        }

        return _fallbackStore.TryGetValue(id, out var lista) ? lista : null;
    }

    public async Task<ListaAsistencia?> ObtenerPorMateriaYFechaAsync(Guid materiaId, DateOnly fecha, CancellationToken ct = default)
    {
        var fechaStr = fecha.ToString("yyyy-MM-dd");
        try
        {
            var response = await _supabaseClient
                .From<ListaAsistenciaDbModel>()
                .Where(x => x.MateriaId == materiaId && x.Fecha == fechaStr)
                .Single(ct);

            if (response != null)
            {
                return MapToEntity(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando lista_asistencia por materia y fecha en Supabase.");
        }

        return _fallbackStore.Values.FirstOrDefault(l => l.MateriaId == materiaId && l.Fecha == fecha);
    }

    public async Task<IReadOnlyList<ListaAsistencia>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<ListaAsistenciaDbModel>()
                .Where(x => x.MateriaId == materiaId)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models.Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error listando asistencias de materia {MateriaId} en Supabase.", materiaId);
        }

        return [.. _fallbackStore.Values.Where(l => l.MateriaId == materiaId)];
    }

    public async Task<ListaAsistencia> CrearOActualizarAsync(ListaAsistencia lista, CancellationToken ct = default)
    {
        if (lista.Id == Guid.Empty)
        {
            lista.Id = Guid.NewGuid();
        }

        lista.ActualizadoEn = DateTimeOffset.UtcNow;
        _fallbackStore[lista.Id] = lista;

        try
        {
            var model = new ListaAsistenciaDbModel
            {
                Id = lista.Id,
                InstitucionId = lista.InstitucionId,
                MateriaId = lista.MateriaId,
                DocenteId = lista.DocenteId,
                EspacioId = lista.EspacioId,
                Fecha = lista.Fecha.ToString("yyyy-MM-dd"),
                HoraInicio = lista.HoraInicio.ToString(@"hh\:mm\:ss"),
                HoraCierre = lista.HoraCierre?.ToString(@"hh\:mm\:ss"),
                Estado = lista.Estado,
                UrlDetalle = lista.UrlDetalle,
                TotalSuscritos = lista.TotalSuscritos,
                TotalPresentes = lista.TotalPresentes,
                TotalFaltas = lista.TotalFaltas,
                CreadoEn = lista.CreadoEn.UtcDateTime,
                ActualizadoEn = lista.ActualizadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<ListaAsistenciaDbModel>()
                .Upsert(model, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error guardando lista_asistencia en Supabase. Se mantiene en almacén local.");
        }

        return lista;
    }

    public async Task ActualizarEstadoAsync(Guid id, string nuevoEstado, TimeSpan? horaCierre = null, CancellationToken ct = default)
    {
        if (_fallbackStore.TryGetValue(id, out var l))
        {
            l.Estado = nuevoEstado;
            if (horaCierre.HasValue) l.HoraCierre = horaCierre.Value;
            l.ActualizadoEn = DateTimeOffset.UtcNow;
        }

        try
        {
            var query = _supabaseClient
                .From<ListaAsistenciaDbModel>()
                .Where(x => x.Id == id)
                .Set(x => x.Estado, nuevoEstado)
                .Set(x => x.ActualizadoEn, DateTime.UtcNow);

            if (horaCierre.HasValue)
            {
                query = query.Set(x => x.HoraCierre!, horaCierre.Value.ToString(@"hh\:mm\:ss"));
            }

            await query.Update(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error actualizando estado de lista_asistencia {Id} en Supabase.", id);
        }
    }

    public async Task ActualizarTotalesAsync(Guid id, int totalSuscritos, int totalPresentes, int totalFaltas, CancellationToken ct = default)
    {
        if (_fallbackStore.TryGetValue(id, out var l))
        {
            l.TotalSuscritos = totalSuscritos;
            l.TotalPresentes = totalPresentes;
            l.TotalFaltas = totalFaltas;
            l.ActualizadoEn = DateTimeOffset.UtcNow;
        }

        try
        {
            await _supabaseClient
                .From<ListaAsistenciaDbModel>()
                .Where(x => x.Id == id)
                .Set(x => x.TotalSuscritos, totalSuscritos)
                .Set(x => x.TotalPresentes, totalPresentes)
                .Set(x => x.TotalFaltas, totalFaltas)
                .Set(x => x.ActualizadoEn, DateTime.UtcNow)
                .Update(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error actualizando totales de lista_asistencia {Id} en Supabase.", id);
        }
    }

    private static ListaAsistencia MapToEntity(ListaAsistenciaDbModel m)
    {
        _ = DateOnly.TryParse(m.Fecha, out var fecha);
        _ = TimeSpan.TryParse(m.HoraInicio, out var horaInicio);
        TimeSpan? horaCierre = TimeSpan.TryParse(m.HoraCierre, out var hc) ? hc : null;

        return new ListaAsistencia
        {
            Id = m.Id,
            InstitucionId = m.InstitucionId,
            MateriaId = m.MateriaId,
            DocenteId = m.DocenteId,
            EspacioId = m.EspacioId,
            Fecha = fecha,
            HoraInicio = horaInicio,
            HoraCierre = horaCierre,
            Estado = m.Estado,
            UrlDetalle = m.UrlDetalle,
            TotalSuscritos = m.TotalSuscritos,
            TotalPresentes = m.TotalPresentes,
            TotalFaltas = m.TotalFaltas,
            CreadoEn = m.CreadoEn,
            ActualizadoEn = m.ActualizadoEn
        };
    }
}
