using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Infrastructure.Data.Models;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseDispositivoRepository(
    Supabase.Client supabaseClient,
    IEstudianteRepository estudianteRepo,
    ILogger<SupabaseDispositivoRepository> logger) : IDispositivoRepository
{
    private readonly Supabase.Client _supabaseClient = supabaseClient;
    private readonly IEstudianteRepository _estudianteRepo = estudianteRepo;
    private readonly ILogger<SupabaseDispositivoRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, Dispositivo> _dispositivosStore = new();
    private static readonly ConcurrentDictionary<Guid, (Guid EstudianteId, Guid InstitucionId, bool Activo)> _vinculacionesStore = new();
    private readonly bool _hasClient = supabaseClient != null;

    public async Task<Dispositivo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        if (_hasClient)
        {
            try
            {
                var response = await _supabaseClient.From<DispositivoDbModel>()
                    .Where(x => x.Id == id)
                    .Single(cancellationToken: ct);

                if (response != null)
                {
                    var disp = MapToEntity(response);
                    _dispositivosStore[disp.Id] = disp;
                    return disp;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error buscando dispositivo {Id} en Supabase.", id);
            }
        }

        _dispositivosStore.TryGetValue(id, out var fallback);
        return fallback;
    }

    public async Task<Dispositivo?> ObtenerPorJtiHashAsync(string jtiHash, CancellationToken ct = default)
    {
        if (_hasClient)
        {
            try
            {
                var response = await _supabaseClient.From<DispositivoDbModel>()
                    .Where(x => x.JtiHash == jtiHash)
                    .Single(cancellationToken: ct);

                if (response != null)
                {
                    var disp = MapToEntity(response);
                    _dispositivosStore[disp.Id] = disp;
                    return disp;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error buscando dispositivo por jti_hash en Supabase.");
            }
        }

        return _dispositivosStore.Values.FirstOrDefault(x => x.JtiHash == jtiHash);
    }

    public async Task<Dispositivo> RegistrarDispositivoAsync(Dispositivo dispositivo, Guid estudianteId, Guid institucionId, CancellationToken ct = default)
    {
        if (dispositivo.Id == Guid.Empty)
        {
            dispositivo.Id = Guid.NewGuid();
        }

        if (_hasClient)
        {
            try
            {
                // 1. Guardar o actualizar registro de dispositivo
                var dispModel = MapToModel(dispositivo);
                var resp = await _supabaseClient.From<DispositivoDbModel>()
                    .Upsert(dispModel, cancellationToken: ct);

                if (resp.Models.Count > 0)
                {
                    dispositivo = MapToEntity(resp.Models[0]);
                }

                // 2. Desactivar vinculaciones previas del estudiante si existen
                await _supabaseClient.From<DispositivoEstudianteDbModel>()
                    .Filter("estudiante_id", Postgrest.Constants.Operator.Equals, estudianteId.ToString())
                    .Filter("institucion_id", Postgrest.Constants.Operator.Equals, institucionId.ToString())
                    .Set(x => x.Activo, false)
                    .Update(cancellationToken: ct);

                // 3. Crear vinculación activa
                var vincModel = new DispositivoEstudianteDbModel
                {
                    Id = Guid.NewGuid(),
                    DispositivoId = dispositivo.Id,
                    EstudianteId = estudianteId,
                    InstitucionId = institucionId,
                    Activo = true,
                    CreadoEn = DateTime.UtcNow
                };
                await _supabaseClient.From<DispositivoEstudianteDbModel>()
                    .Insert(vincModel, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error registrando dispositivo y vinculación en Supabase. Se almacena localmente.");
            }
        }

        _dispositivosStore[dispositivo.Id] = dispositivo;
        _vinculacionesStore[dispositivo.Id] = (estudianteId, institucionId, true);
        return dispositivo;
    }

    public async Task<Estudiante?> ObtenerEstudiantePorDispositivoAsync(Guid dispositivoId, Guid institucionId, CancellationToken ct = default)
    {
        if (_hasClient)
        {
            try
            {
                var response = await _supabaseClient.From<DispositivoEstudianteDbModel>()
                    .Filter("dispositivo_id", Postgrest.Constants.Operator.Equals, dispositivoId.ToString())
                    .Filter("institucion_id", Postgrest.Constants.Operator.Equals, institucionId.ToString())
                    .Filter("activo", Postgrest.Constants.Operator.Equals, "true")
                    .Get(cancellationToken: ct);

                var vinculacion = response.Models.FirstOrDefault();
                if (vinculacion != null)
                {
                    return await _estudianteRepo.ObtenerPorIdAsync(vinculacion.EstudianteId, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error obteniendo estudiante para dispositivo {DispositivoId} en Supabase.", dispositivoId);
            }
        }

        if (_vinculacionesStore.TryGetValue(dispositivoId, out var vinc) && vinc.InstitucionId == institucionId && vinc.Activo)
        {
            return await _estudianteRepo.ObtenerPorIdAsync(vinc.EstudianteId, ct);
        }

        return null;
    }

    public async Task ActualizarUltimoUsoAsync(Guid dispositivoId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (_hasClient)
        {
            try
            {
                await _supabaseClient.From<DispositivoDbModel>()
                    .Filter("id", Postgrest.Constants.Operator.Equals, dispositivoId.ToString())
                    .Set(x => x.UltimoUsoEn, now)
                    .Update(cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error actualizando ultimo_uso_en para dispositivo {DispositivoId} en Supabase.", dispositivoId);
            }
        }

        if (_dispositivosStore.TryGetValue(dispositivoId, out var disp))
        {
            disp.UltimoUsoEn = new DateTimeOffset(now, TimeSpan.Zero);
        }
    }

    public async Task RevocarDispositivoAsync(Guid dispositivoId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (_hasClient)
        {
            try
            {
                await _supabaseClient.From<DispositivoDbModel>()
                    .Filter("id", Postgrest.Constants.Operator.Equals, dispositivoId.ToString())
                    .Set(x => x.RevocadoEn!, now)
                    .Update(cancellationToken: ct);

                await _supabaseClient.From<DispositivoEstudianteDbModel>()
                    .Filter("dispositivo_id", Postgrest.Constants.Operator.Equals, dispositivoId.ToString())
                    .Set(x => x.Activo, false)
                    .Update(cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error revocando dispositivo {DispositivoId} en Supabase.", dispositivoId);
            }
        }

        if (_dispositivosStore.TryGetValue(dispositivoId, out var disp))
        {
            disp.RevocadoEn = new DateTimeOffset(now, TimeSpan.Zero);
        }
        if (_vinculacionesStore.TryGetValue(dispositivoId, out var vinc))
        {
            _vinculacionesStore[dispositivoId] = (vinc.EstudianteId, vinc.InstitucionId, false);
        }
    }

    public async Task<DispositivoEstudiante?> ObtenerVinculacionActivaAsync(Guid estudianteId, Guid institucionId, CancellationToken ct = default)
    {
        if (_hasClient)
        {
            try
            {
                var response = await _supabaseClient.From<DispositivoEstudianteDbModel>()
                    .Filter("estudiante_id", Postgrest.Constants.Operator.Equals, estudianteId.ToString())
                    .Filter("institucion_id", Postgrest.Constants.Operator.Equals, institucionId.ToString())
                    .Filter("activo", Postgrest.Constants.Operator.Equals, "true")
                    .Get(cancellationToken: ct);

                var vinculacion = response.Models.FirstOrDefault();
                if (vinculacion != null)
                {
                    return new DispositivoEstudiante
                    {
                        Id = vinculacion.Id,
                        DispositivoId = vinculacion.DispositivoId,
                        EstudianteId = vinculacion.EstudianteId,
                        InstitucionId = vinculacion.InstitucionId,
                        Activo = vinculacion.Activo,
                        CreadoEn = new DateTimeOffset(vinculacion.CreadoEn, TimeSpan.Zero)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error buscando vinculación activa para estudiante {EstudianteId}.", estudianteId);
            }
        }

        var entry = _vinculacionesStore.FirstOrDefault(x => x.Value.EstudianteId == estudianteId && x.Value.InstitucionId == institucionId && x.Value.Activo);
        if (entry.Key != Guid.Empty)
        {
            return new DispositivoEstudiante
            {
                Id = Guid.NewGuid(),
                DispositivoId = entry.Key,
                EstudianteId = estudianteId,
                InstitucionId = institucionId,
                Activo = true,
                CreadoEn = DateTimeOffset.UtcNow
            };
        }

        return null;
    }

    private static Dispositivo MapToEntity(DispositivoDbModel model) => new()
    {
        Id = model.Id,
        JtiHash = model.JtiHash,
        Kid = model.Kid,
        EmitidoEn = new DateTimeOffset(model.EmitidoEn, TimeSpan.Zero),
        UltimoUsoEn = new DateTimeOffset(model.UltimoUsoEn, TimeSpan.Zero),
        RevocadoEn = model.RevocadoEn.HasValue ? new DateTimeOffset(model.RevocadoEn.Value, TimeSpan.Zero) : null,
        AgenteResumen = model.AgenteResumen
    };

    private static DispositivoDbModel MapToModel(Dispositivo entity) => new()
    {
        Id = entity.Id,
        JtiHash = entity.JtiHash,
        Kid = entity.Kid,
        EmitidoEn = entity.EmitidoEn.UtcDateTime,
        UltimoUsoEn = entity.UltimoUsoEn.UtcDateTime,
        RevocadoEn = entity.RevocadoEn?.UtcDateTime,
        AgenteResumen = entity.AgenteResumen
    };
}
