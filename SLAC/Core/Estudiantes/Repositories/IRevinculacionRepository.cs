using SLAC.Core.Estudiantes.Entities;

namespace SLAC.Core.Estudiantes.Repositories;

/// <summary>
/// Contrato de persistencia para solicitudes de revinculación de dispositivos (SRS RN-05).
/// </summary>
public interface IRevinculacionRepository
{
    Task<Revinculacion> CrearSolicitudAsync(Revinculacion solicitud, CancellationToken ct = default);
    Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default);
    Task<Revinculacion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Revinculacion?> ObtenerPendientePorEstudianteYMateriaAsync(Guid estudianteId, Guid materiaId, CancellationToken ct = default);
    Task MarcarComoUsadaAsync(Guid id, CancellationToken ct = default);
}
