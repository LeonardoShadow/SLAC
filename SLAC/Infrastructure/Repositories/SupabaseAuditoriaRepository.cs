using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseAuditoriaRepository(Client supabaseClient, ILogger<SupabaseAuditoriaRepository> logger) : IAuditoriaRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseAuditoriaRepository> _logger = logger;
    private static readonly ConcurrentBag<Auditoria> _fallbackStore = [];

    public async Task RegistrarEventoAsync(Auditoria auditoria, CancellationToken ct = default)
    {
        if (auditoria.Id == Guid.Empty)
        {
            auditoria.Id = Guid.NewGuid();
        }

        auditoria.CreadoEn = DateTimeOffset.UtcNow;
        _fallbackStore.Add(auditoria);

        try
        {
            var model = new AuditoriaDbModel
            {
                Id = auditoria.Id,
                InstitucionId = auditoria.InstitucionId,
                Actor = auditoria.Actor,
                Evento = auditoria.Evento,
                Entidad = auditoria.Entidad,
                Datos = auditoria.DatosJson,
                CreadoEn = auditoria.CreadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<AuditoriaDbModel>()
                .Insert(model, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error registrando auditoría en Supabase. Se mantiene en memoria local.");
        }
    }

    public async Task<IReadOnlyList<Auditoria>> ListarPorInstitucionAsync(Guid institucionId, int limite = 50, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<AuditoriaDbModel>()
                .Where(x => x.InstitucionId == institucionId)
                .Order("creado_en", Postgrest.Constants.Ordering.Descending)
                .Limit(limite)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models.Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando auditoría en Supabase. Usando almacén local.");
        }

        return [.. _fallbackStore
            .Where(a => a.InstitucionId == institucionId)
            .OrderByDescending(a => a.CreadoEn)
            .Take(limite)];
    }

    public async Task<IReadOnlyList<Auditoria>> ListarPorEntidadAsync(string entidad, Guid institucionId, int limite = 50, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<AuditoriaDbModel>()
                .Where(x => x.Entidad == entidad && x.InstitucionId == institucionId)
                .Order("creado_en", Postgrest.Constants.Ordering.Descending)
                .Limit(limite)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return [.. response.Models.Select(MapToEntity)];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando auditoría por entidad en Supabase.");
        }

        return [.. _fallbackStore
            .Where(a => a.Entidad == entidad && a.InstitucionId == institucionId)
            .OrderByDescending(a => a.CreadoEn)
            .Take(limite)];
    }

    private static Auditoria MapToEntity(AuditoriaDbModel m) => new()
    {
        Id = m.Id,
        InstitucionId = m.InstitucionId,
        Actor = m.Actor,
        Evento = m.Evento,
        Entidad = m.Entidad,
        DatosJson = m.Datos,
        CreadoEn = m.CreadoEn
    };
}
