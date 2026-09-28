using SLAC.Core.Estudiantes.Entities;

namespace SLAC.Core.Attendance.Services;

public record SolicitudRevinculacionResult(
    bool SolicitudCreada,
    bool YaAutorizada,
    Guid? SolicitudId,
    string? Mensaje,
    string? EstudianteNombre,
    string? EstudianteCodigo);

/// <summary>
/// Contrato de servicio para el flujo de autorización de revinculación de dispositivos (Paso 38).
/// </summary>
public interface IRevinculacionService
{
    Task<SolicitudRevinculacionResult> SolicitarRevinculacionAsync(
        Guid sesionId,
        string codigoEstudiante,
        string userAgent,
        CancellationToken ct = default);

    Task<(bool Exito, string? Error)> AutorizarRevinculacionAsync(
        Guid solicitudId,
        string actorDocente,
        CancellationToken ct = default);

    Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(
        Guid materiaId,
        CancellationToken ct = default);
}
