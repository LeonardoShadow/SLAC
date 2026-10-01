using System.Collections.Concurrent;
using SLAC.Core.Institucional.Entities;
using SLAC.Core.Institucional.Repositories;

namespace SLAC.Infrastructure.Repositories;

public class InMemoryAdministradorInstitucionalRepository : IAdministradorInstitucionalRepository
{
    private static readonly ConcurrentDictionary<Guid, AdministradorInstitucional> _store = new();

    static InMemoryAdministradorInstitucionalRepository()
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
        _store[adminUpds.Id] = adminUpds;
    }

    public Task<IReadOnlyList<AdministradorInstitucional>> ListarTodosAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AdministradorInstitucional> list = [.. _store.Values.OrderBy(a => a.NombreCompleto)];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<AdministradorInstitucional>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
    {
        IReadOnlyList<AdministradorInstitucional> list = [.. _store.Values.Where(a => a.InstitucionId == institucionId).OrderBy(a => a.NombreCompleto)];
        return Task.FromResult(list);
    }

    public Task<AdministradorInstitucional?> ValidarCredencialesAsync(string correo, string codigoPin, CancellationToken ct = default)
    {
        var correoNorm = correo.Trim().ToLowerInvariant();
        var userPart = correoNorm.Split('@')[0];

        var admin = _store.Values.FirstOrDefault(a =>
            a.Activo &&
            a.CodigoPin == codigoPin.Trim() &&
            (
                a.Correo.Equals(correoNorm, StringComparison.OrdinalIgnoreCase) ||
                a.Correo.Split('@')[0].Equals(userPart, StringComparison.OrdinalIgnoreCase) ||
                correoNorm == "admin"
            )
        );

        return Task.FromResult(admin);
    }

    public Task<AdministradorInstitucional> GuardarAsync(AdministradorInstitucional admin, CancellationToken ct = default)
    {
        if (admin.Id == Guid.Empty)
        {
            admin.Id = Guid.NewGuid();
        }
        _store[admin.Id] = admin;
        return Task.FromResult(admin);
    }

    public Task<bool> CambiarEstadoAsync(Guid id, bool activo, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var a))
        {
            a.Activo = activo;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
