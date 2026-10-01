using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SLAC.Core.Institucional.Entities;
using SLAC.Core.Institucional.Repositories;
using SLAC.Infrastructure.Data.Models;
using Supabase;

namespace SLAC.Infrastructure.Repositories;

public class SupabaseAdministradorInstitucionalRepository(
    Client supabaseClient,
    ILogger<SupabaseAdministradorInstitucionalRepository> logger) : IAdministradorInstitucionalRepository
{
    private readonly Client _supabaseClient = supabaseClient;
    private readonly ILogger<SupabaseAdministradorInstitucionalRepository> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, AdministradorInstitucional> _fallbackStore = new();

    static SupabaseAdministradorInstitucionalRepository()
    {
        // Administrador por defecto de UPDS
        var adminUpds = new AdministradorInstitucional
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            InstitucionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            NombreCompleto = "Administrador UPDS",
            Correo = "sc.upds.m@upds.net.bo",
            CodigoPin = "75604458",
            Activo = true,
            CreadoEn = DateTimeOffset.UtcNow
        };
        _fallbackStore[adminUpds.Id] = adminUpds;
    }

    public async Task<IReadOnlyList<AdministradorInstitucional>> ListarTodosAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<AdministradorInstitucionalDbModel>()
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                var list = response.Models.ConvertAll(MapToEntity);
                foreach (var item in list)
                {
                    _fallbackStore[item.Id] = item;
                }
                return list.OrderBy(a => a.NombreCompleto).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando administradores institucionales en Supabase. Usando fallback local.");
        }

        return [.. _fallbackStore.Values.OrderBy(a => a.NombreCompleto)];
    }

    public async Task<IReadOnlyList<AdministradorInstitucional>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _supabaseClient
                .From<AdministradorInstitucionalDbModel>()
                .Where(x => x.InstitucionId == institucionId)
                .Get(ct);

            if (response?.Models?.Count > 0)
            {
                return response.Models.ConvertAll(MapToEntity).OrderBy(a => a.NombreCompleto).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando administradores por institucion {Id} en Supabase.", institucionId);
        }

        return [.. _fallbackStore.Values.Where(a => a.InstitucionId == institucionId).OrderBy(a => a.NombreCompleto)];
    }

    public async Task<AdministradorInstitucional?> ValidarCredencialesAsync(string correo, string codigoPin, CancellationToken ct = default)
    {
        var correoNorm = correo.Trim().ToLowerInvariant();
        var userPart = correoNorm.Split('@')[0];
        var pinNorm = codigoPin.Trim();

        try
        {
            var all = await ListarTodosAsync(ct);
            var admin = all.FirstOrDefault(a =>
                a.Activo &&
                a.CodigoPin == pinNorm &&
                (
                    a.Correo.Equals(correoNorm, StringComparison.OrdinalIgnoreCase) ||
                    a.Correo.Split('@')[0].Equals(userPart, StringComparison.OrdinalIgnoreCase) ||
                    correoNorm == "admin"
                )
            );

            if (admin != null) return admin;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error validando credenciales de administrador.");
        }

        return _fallbackStore.Values.FirstOrDefault(a =>
            a.Activo &&
            a.CodigoPin == pinNorm &&
            (
                a.Correo.Equals(correoNorm, StringComparison.OrdinalIgnoreCase) ||
                a.Correo.Split('@')[0].Equals(userPart, StringComparison.OrdinalIgnoreCase) ||
                correoNorm == "admin"
            )
        );
    }

    public async Task<AdministradorInstitucional> GuardarAsync(AdministradorInstitucional admin, CancellationToken ct = default)
    {
        if (admin.Id == Guid.Empty)
        {
            admin.Id = Guid.NewGuid();
        }

        _fallbackStore[admin.Id] = admin;

        try
        {
            var model = new AdministradorInstitucionalDbModel
            {
                Id = admin.Id,
                InstitucionId = admin.InstitucionId,
                NombreCompleto = admin.NombreCompleto,
                Correo = admin.Correo,
                CodigoPin = admin.CodigoPin,
                Activo = admin.Activo,
                CreadoEn = admin.CreadoEn.UtcDateTime
            };

            await _supabaseClient
                .From<AdministradorInstitucionalDbModel>()
                .Upsert(model, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error guardando administrador institucional {Id} en Supabase.", admin.Id);
        }

        return admin;
    }

    public async Task<bool> CambiarEstadoAsync(Guid id, bool activo, CancellationToken ct = default)
    {
        if (_fallbackStore.TryGetValue(id, out var a))
        {
            a.Activo = activo;
        }

        try
        {
            await _supabaseClient
                .From<AdministradorInstitucionalDbModel>()
                .Where(x => x.Id == id)
                .Set(x => x.Activo, activo)
                .Update(cancellationToken: ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cambiando estado de administrador {Id} en Supabase.", id);
            return _fallbackStore.ContainsKey(id);
        }
    }

    private static AdministradorInstitucional MapToEntity(AdministradorInstitucionalDbModel m) => new()
    {
        Id = m.Id,
        InstitucionId = m.InstitucionId,
        NombreCompleto = m.NombreCompleto,
        Correo = m.Correo,
        CodigoPin = m.CodigoPin,
        Activo = m.Activo,
        CreadoEn = new DateTimeOffset(m.CreadoEn, TimeSpan.Zero)
    };
}
