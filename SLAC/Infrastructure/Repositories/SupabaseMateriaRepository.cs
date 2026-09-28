using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Docentes.Entities;
using SLAC.Core.Docentes.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseMateriaRepository(Client supabaseClient, ILogger<SupabaseMateriaRepository> logger) : IMateriaRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseMateriaRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, Materia> _fallbackStore = new();

    public async Task<IReadOnlyList<Materia>> ListarPorDocenteAsync(Guid docenteId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<MateriaDbModel>()
                .Where(x => x.DocenteId == docenteId)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models.Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando materias de docente {DocenteId} en Supabase. Usando almacén local.", docenteId);
        }

        return [.. _fallbackStore.Values.Where(m => m.DocenteId == docenteId)];
    }

    public async Task<IReadOnlyList<Materia>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<MateriaDbModel>()
                .Where(x => x.InstitucionId == institucionId)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models.Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando materias por institución en Supabase. Usando almacén local.");
        }

        return [.. _fallbackStore.Values.Where(m => m.InstitucionId == institucionId)];
    }

    public async Task<IReadOnlyList<Materia>> ListarActivasPorDiaAsync(Guid institucionId, string diaSigla, CancellationToken ct = default)
    {
        diaSigla = diaSigla.Trim().ToUpperInvariant();
        try
        {
            var response = await _supabaseClient
                .From<MateriaDbModel>()
                .Where(x => x.InstitucionId == institucionId && x.Estado == "activa")
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models
                    .Select(MapToEntity)
                    .Where(m => m.ObtenerListaDias().Contains(diaSigla))];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando materias activas para el día {Dia} en Supabase.", diaSigla);
        }

        return [.. _fallbackStore.Values
            .Where(m => m.InstitucionId == institucionId &&
                        m.Estado == "activa" &&
                        m.ObtenerListaDias().Contains(diaSigla))];
    }

    public async Task<Materia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<MateriaDbModel>()
                .Where(x => x.Id == id)
                .Single(ct);

            if (response != null)
            {
                return MapToEntity(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando materia {Id} en Supabase. Usando almacén local.", id);
        }

        return _fallbackStore.TryGetValue(id, out var mat) ? mat : null;
    }

    public async Task<Materia> GuardarAsync(Materia materia, CancellationToken ct = default)
    {
        if (materia.Id == Guid.Empty)
        {
            materia.Id = Guid.NewGuid();
        }

        _fallbackStore[materia.Id] = materia;

        try
        {
            var model = new MateriaDbModel
            {
                Id = materia.Id,
                InstitucionId = materia.InstitucionId,
                DocenteId = materia.DocenteId,
                PeriodoId = materia.PeriodoId,
                Codigo = materia.Codigo,
                Nombre = materia.Nombre,
                Grupo = materia.Grupo,
                EspacioId = materia.EspacioId,
                HoraInicio = materia.HoraInicio.ToString(@"hh\:mm\:ss"),
                Dias = materia.Dias,
                Estado = materia.Estado,
                CreadoEn = materia.CreadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<MateriaDbModel>()
                .Upsert(model, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error guardando materia en Supabase. Se mantiene en almacén local.");
        }

        return materia;
    }

    public async Task<bool> ArchivarAsync(Guid id, CancellationToken ct = default)
    {
        if (_fallbackStore.TryGetValue(id, out var m))
        {
            m.Estado = "archivada";
        }

        try
        {
            await _supabaseClient
                .From<MateriaDbModel>()
                .Where(x => x.Id == id)
                .Set(x => x.Estado, "archivada")
                .Update(cancellationToken: ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error archivando materia {Id} en Supabase. Se actualizó en almacén local.", id);
            return _fallbackStore.ContainsKey(id);
        }
    }

    private static Materia MapToEntity(MateriaDbModel m)
    {
        _ = TimeSpan.TryParse(m.HoraInicio, out var hora);
        return new Materia
        {
            Id = m.Id,
            InstitucionId = m.InstitucionId,
            DocenteId = m.DocenteId,
            PeriodoId = m.PeriodoId,
            Codigo = m.Codigo,
            Nombre = m.Nombre,
            Grupo = m.Grupo,
            EspacioId = m.EspacioId,
            HoraInicio = hora,
            Dias = m.Dias,
            Estado = m.Estado,
            CreadoEn = m.CreadoEn
        };
    }
}
