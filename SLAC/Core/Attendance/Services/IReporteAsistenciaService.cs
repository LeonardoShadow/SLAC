namespace SLAC.Core.Attendance.Services;

/// <summary>
/// Modelo de datos para una inasistencia (falta) de un estudiante en un reporte docente (SRS 4.7).
/// </summary>
public record ItemReporteFalta(
    Guid DetalleId,
    Guid ListaId,
    Guid EstudianteId,
    string CodigoEstudiante,
    string NombreEstudiante,
    string CorreoEstudiante,
    Guid MateriaId,
    string NombreMateria,
    string CodigoMateria,
    DateOnly Fecha,
    TimeSpan HoraInicio,
    string Estado,
    string? Origen,
    DateTimeOffset CreadoEn
);

/// <summary>
/// Servicio para la consulta y exportación de reportes de faltas para docentes (ClosedXML y CSV).
/// </summary>
public interface IReporteAsistenciaService
{
    /// <summary>
    /// Obtiene la lista de faltas registradas para una materia en un rango de fechas.
    /// </summary>
    Task<IReadOnlyList<ItemReporteFalta>> ObtenerReporteFaltasAsync(
        Guid materiaId,
        DateOnly? fechaDesde = null,
        DateOnly? fechaHasta = null,
        CancellationToken ct = default);

    /// <summary>
    /// Genera un archivo Excel (.xlsx) formateado con ClosedXML a partir de los datos de faltas.
    /// </summary>
    Task<byte[]> GenerarExcelFaltasAsync(
        IReadOnlyList<ItemReporteFalta> faltas,
        string materiaNombre,
        string materiaCodigo,
        CancellationToken ct = default);

    /// <summary>
    /// Genera un archivo CSV codificado en UTF-8 con BOM a partir de los datos de faltas.
    /// </summary>
    Task<byte[]> GenerarCsvFaltasAsync(
        IReadOnlyList<ItemReporteFalta> faltas,
        CancellationToken ct = default);
}
