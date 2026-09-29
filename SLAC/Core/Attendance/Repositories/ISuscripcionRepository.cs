using SLAC.Core.Attendance.Entities;

namespace SLAC.Core.Attendance.Repositories;

public interface ISuscripcionRepository
{
    Task<IReadOnlyList<Suscripcion>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListarEstudiantesIdsPorMateriaAsync(Guid materiaId, CancellationToken ct = default);
    Task<Suscripcion?> ObtenerAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default);
    Task<Suscripcion> SuscribirAsync(Suscripcion suscripcion, CancellationToken ct = default);
    Task<bool> DesinscribirAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default);
}
