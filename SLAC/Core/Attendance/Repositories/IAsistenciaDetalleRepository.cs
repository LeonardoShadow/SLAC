using SLAC.Core.Attendance.Entities;

namespace SLAC.Core.Attendance.Repositories;

public interface IAsistenciaDetalleRepository
{
    Task<IReadOnlyList<AsistenciaDetalle>> ListarPorListaAsync(Guid listaId, CancellationToken ct = default);
    Task<AsistenciaDetalle?> ObtenerPorListaYEstudianteAsync(Guid listaId, Guid estudianteId, CancellationToken ct = default);
    Task<AsistenciaDetalle> RegistrarAsistenciaAsync(AsistenciaDetalle detalle, CancellationToken ct = default);
    Task<int> RegistrarFaltasIdempotenteAsync(Guid listaId, Guid institucionId, IEnumerable<Guid> estudiantesIds, CancellationToken ct = default);
    Task EliminarPorListaAsync(Guid listaId, CancellationToken ct = default);
}
