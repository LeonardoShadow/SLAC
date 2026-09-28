using SLAC.Core.Attendance.Entities;

namespace SLAC.Core.Attendance.Repositories;

public interface IListaAsistenciaRepository
{
    Task<ListaAsistencia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<ListaAsistencia?> ObtenerPorMateriaYFechaAsync(Guid materiaId, DateOnly fecha, CancellationToken ct = default);
    Task<IReadOnlyList<ListaAsistencia>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default);
    Task<ListaAsistencia> CrearOActualizarAsync(ListaAsistencia lista, CancellationToken ct = default);
    Task ActualizarEstadoAsync(Guid id, string nuevoEstado, TimeSpan? horaCierre = null, CancellationToken ct = default);
    Task ActualizarTotalesAsync(Guid id, int totalSuscritos, int totalPresentes, int totalFaltas, CancellationToken ct = default);
}
