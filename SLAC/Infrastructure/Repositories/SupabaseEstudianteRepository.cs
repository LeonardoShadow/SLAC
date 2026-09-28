using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Infrastructure.Data.Models;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseEstudianteRepository(
    Supabase.Client supabaseClient,
    ILogger<SupabaseEstudianteRepository> logger) : IEstudianteRepository
{
    private readonly Supabase.Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseEstudianteRepository> _logger = logger;
    private readonly ConcurrentDictionary<Guid, Estudiante> _fallbackStore = new();

    public async Task<Estudiante?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient.From<EstudianteDbModel>()
                .Where(x => x.Id == id)
                .Single(cancellationToken: ct);

            if (response != null)
            {
                var estudiante = MapToEntity(response);
                _fallbackStore[estudiante.Id] = estudiante;
                return estudiante;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al obtener estudiante {Id} desde Supabase. Buscando en fallback.", id);
        }

        _fallbackStore.TryGetValue(id, out var fallback);
        return fallback;
    }

    public async Task<Estudiante?> ObtenerPorCodigoAsync(Guid institucionId, string codigo, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient.From<EstudianteDbModel>()
                .Where(x => x.InstitucionId == institucionId && x.Codigo == codigo)
                .Single(cancellationToken: ct);

            if (response != null)
            {
                var estudiante = MapToEntity(response);
                _fallbackStore[estudiante.Id] = estudiante;
                return estudiante;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al buscar estudiante por código {Codigo} en Supabase.", codigo);
        }

        return _fallbackStore.Values.FirstOrDefault(x => x.InstitucionId == institucionId && x.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Estudiante?> ObtenerPorCorreoAsync(Guid institucionId, string correo, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient.From<EstudianteDbModel>()
                .Where(x => x.InstitucionId == institucionId && x.Correo == correo)
                .Single(cancellationToken: ct);

            if (response != null)
            {
                var estudiante = MapToEntity(response);
                _fallbackStore[estudiante.Id] = estudiante;
                return estudiante;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al buscar estudiante por correo {Correo} en Supabase.", correo);
        }

        return _fallbackStore.Values.FirstOrDefault(x => x.InstitucionId == institucionId && x.Correo.Equals(correo, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Estudiante> CrearOActualizarAsync(Estudiante estudiante, CancellationToken ct = default)
    {
        if (estudiante.Id == Guid.Empty)
        {
            estudiante.Id = Guid.NewGuid();
        }

        try
        {
            var model = MapToModel(estudiante);
            var response = await _supabaseClient.From<EstudianteDbModel>()
                .Upsert(model, cancellationToken: ct);

            if (response.Models.Count > 0)
            {
                var persisted = MapToEntity(response.Models[0]);
                _fallbackStore[persisted.Id] = persisted;
                return persisted;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al persistir estudiante {Codigo} en Supabase. Guardando en memoria.", estudiante.Codigo);
        }

        _fallbackStore[estudiante.Id] = estudiante;
        return estudiante;
    }

    public async Task<IReadOnlyList<Estudiante>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient.From<EstudianteDbModel>()
                .Where(x => x.InstitucionId == institucionId)
                .Get(cancellationToken: ct);

            if (response.Models.Count > 0)
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
            _logger.LogWarning(ex, "Error al listar estudiantes de institución {InstitucionId} en Supabase.", institucionId);
        }

        return [.. _fallbackStore.Values.Where(x => x.InstitucionId == institucionId)];
    }

    private static Estudiante MapToEntity(EstudianteDbModel model) => new()
    {
        Id = model.Id,
        InstitucionId = model.InstitucionId,
        Codigo = model.Codigo,
        Nombres = model.Nombres,
        Apellidos = model.Apellidos,
        Correo = model.Correo,
        ConsentimientoEn = model.ConsentimientoEn.HasValue ? new DateTimeOffset(model.ConsentimientoEn.Value, TimeSpan.Zero) : null,
        CreadoEn = new DateTimeOffset(model.CreadoEn, TimeSpan.Zero)
    };

    private static EstudianteDbModel MapToModel(Estudiante entity) => new()
    {
        Id = entity.Id,
        InstitucionId = entity.InstitucionId,
        Codigo = entity.Codigo,
        Nombres = entity.Nombres,
        Apellidos = entity.Apellidos,
        Correo = entity.Correo,
        ConsentimientoEn = entity.ConsentimientoEn?.UtcDateTime,
        CreadoEn = entity.CreadoEn.UtcDateTime
    };
}
