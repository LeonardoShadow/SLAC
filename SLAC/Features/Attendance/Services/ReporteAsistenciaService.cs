using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Estudiantes.Repositories;

namespace SLAC.Features.Attendance.Services;

/// <summary>
/// Implementación del servicio de reportes de faltas para docentes (SRS 4.7 y PRD).
/// Genera libros Excel institucionales con ClosedXML y archivos CSV con BOM UTF-8.
/// </summary>
public class ReporteAsistenciaService(
    IListaAsistenciaRepository listaRepo,
    IAsistenciaDetalleRepository detalleRepo,
    IEstudianteRepository estudianteRepo,
    IMateriaRepository materiaRepo,
    ILogger<ReporteAsistenciaService> logger) : IReporteAsistenciaService
{
    private readonly IListaAsistenciaRepository _listaRepo = listaRepo;
    private readonly IAsistenciaDetalleRepository _detalleRepo = detalleRepo;
    private readonly IEstudianteRepository _estudianteRepo = estudianteRepo;
    private readonly IMateriaRepository _materiaRepo = materiaRepo;
    private readonly ILogger<ReporteAsistenciaService> _logger = logger;

    public async Task<IReadOnlyList<ItemReporteFalta>> ObtenerReporteFaltasAsync(
        Guid materiaId,
        DateOnly? fechaDesde = null,
        DateOnly? fechaHasta = null,
        CancellationToken ct = default)
    {
        var result = new List<ItemReporteFalta>();

        var materia = await _materiaRepo.ObtenerPorIdAsync(materiaId, ct);
        var materiaNombre = materia?.Nombre ?? "Materia";
        var materiaCodigo = materia?.Codigo ?? "";

        var listas = await _listaRepo.ListarPorMateriaAsync(materiaId, ct);
        if (fechaDesde.HasValue)
        {
            listas = [.. listas.Where(l => l.Fecha >= fechaDesde.Value)];
        }
        if (fechaHasta.HasValue)
        {
            listas = [.. listas.Where(l => l.Fecha <= fechaHasta.Value)];
        }

        var estudiantesCache = new Dictionary<Guid, (string Codigo, string Nombre, string Correo)>();

        foreach (var lista in listas)
        {
            var detalles = await _detalleRepo.ListarPorListaAsync(lista.Id, ct);
            var faltas = detalles.Where(d => d.Estado == "Falta");

            foreach (var falta in faltas)
            {
                if (!estudiantesCache.TryGetValue(falta.EstudianteId, out var estInfo))
                {
                    var est = await _estudianteRepo.ObtenerPorIdAsync(falta.EstudianteId, ct);
                    estInfo = est != null
                        ? (est.Codigo, est.NombreCompleto, est.Correo)
                        : ("N/D", "Estudiante No Registrado", "N/D");
                    estudiantesCache[falta.EstudianteId] = estInfo;
                }

                result.Add(new ItemReporteFalta(
                    DetalleId: falta.Id,
                    ListaId: lista.Id,
                    EstudianteId: falta.EstudianteId,
                    CodigoEstudiante: estInfo.Codigo,
                    NombreEstudiante: estInfo.Nombre,
                    CorreoEstudiante: estInfo.Correo,
                    MateriaId: materiaId,
                    NombreMateria: materiaNombre,
                    CodigoMateria: materiaCodigo,
                    Fecha: lista.Fecha,
                    HoraInicio: lista.HoraInicio,
                    Estado: falta.Estado,
                    Origen: falta.Origen,
                    CreadoEn: falta.CreadoEn
                ));
            }
        }

        _logger.LogInformation("Reporte de faltas generado para materia {MateriaId}: {Count} inasistencias encontradas.", materiaId, result.Count);
        return [.. result.OrderByDescending(r => r.Fecha).ThenBy(r => r.NombreEstudiante)];
    }

    public async Task<byte[]> GenerarExcelFaltasAsync(
        IReadOnlyList<ItemReporteFalta> faltas,
        string materiaNombre,
        string materiaCodigo,
        CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Faltas");

        // Configuración de visualización de cuadrícula
        worksheet.ShowGridLines = true;

        // Título del Reporte
        worksheet.Cell(1, 1).Value = "SLAC - Sistema de Asistencia a Clases";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 14;
        worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#312E81");

        worksheet.Cell(2, 1).Value = $"Reporte Oficial de Inasistencias (Solo Faltas) - {materiaNombre} ({materiaCodigo})";
        worksheet.Cell(2, 1).Style.Font.Bold = true;
        worksheet.Cell(2, 1).Style.Font.FontSize = 11;
        worksheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#4F46E5");

        worksheet.Cell(3, 1).Value = $"Fecha de Emisión: {DateTime.Now:dd/MM/yyyy HH:mm:ss} | Total Inasistencias: {faltas.Count}";
        worksheet.Cell(3, 1).Style.Font.Italic = true;
        worksheet.Cell(3, 1).Style.Font.FontSize = 9;
        worksheet.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;

        // Encabezados de tabla
        var headers = new[]
        {
            "N°",
            "Código Estudiante",
            "Estudiante",
            "Correo Institucional",
            "Fecha Sesión",
            "Hora Inicio",
            "Estado",
            "Origen Cómputo",
            "Fecha Cómputo"
        };

        const int startRow = 5;
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(startRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4338CA");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        // Filas de datos
        int currentRow = startRow + 1;
        for (int i = 0; i < faltas.Count; i++)
        {
            var item = faltas[i];
            var isEven = i % 2 == 0;
            var rowColor = isEven ? XLColor.White : XLColor.FromHtml("#F8FAFC");

            worksheet.Cell(currentRow, 1).Value = i + 1;
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            worksheet.Cell(currentRow, 2).Value = item.CodigoEstudiante;
            worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            worksheet.Cell(currentRow, 3).Value = item.NombreEstudiante;
            worksheet.Cell(currentRow, 4).Value = item.CorreoEstudiante;

            worksheet.Cell(currentRow, 5).Value = item.Fecha.ToString("dd/MM/yyyy");
            worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            worksheet.Cell(currentRow, 6).Value = item.HoraInicio.ToString(@"hh\:mm");
            worksheet.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var estadoCell = worksheet.Cell(currentRow, 7);
            estadoCell.Value = item.Estado;
            estadoCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            estadoCell.Style.Font.Bold = true;
            estadoCell.Style.Font.FontColor = XLColor.FromHtml("#DC2626"); // Rojo para faltas

            worksheet.Cell(currentRow, 8).Value = item.Origen ?? "Ausente";
            worksheet.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            worksheet.Cell(currentRow, 9).Value = item.CreadoEn.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            worksheet.Cell(currentRow, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Aplicar estilo de fondo
            for (int col = 1; col <= headers.Length; col++)
            {
                worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = rowColor;
                worksheet.Cell(currentRow, col).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                worksheet.Cell(currentRow, col).Style.Border.BottomBorderColor = XLColor.FromHtml("#E2E8F0");
            }

            currentRow++;
        }

        // Autoajuste de columnas
        worksheet.Columns().AdjustToContents();

        await using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }

    public async Task<byte[]> GenerarCsvFaltasAsync(
        IReadOnlyList<ItemReporteFalta> faltas,
        CancellationToken ct = default)
    {
        await using var memoryStream = new MemoryStream();
        // UTF-8 con BOM para que Excel en Windows lo abra reconociendo tildes y caracteres especiales
        await using (var writer = new StreamWriter(memoryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1024, leaveOpen: true))
        {
            await writer.WriteLineAsync("Nro,Codigo,Estudiante,Correo,Fecha,HoraInicio,Estado,Origen,FechaComputo");

            for (int i = 0; i < faltas.Count; i++)
            {
                var f = faltas[i];
                var codigo = EscapeCsv(f.CodigoEstudiante);
                var nombre = EscapeCsv(f.NombreEstudiante);
                var correo = EscapeCsv(f.CorreoEstudiante);
                var fecha = f.Fecha.ToString("dd/MM/yyyy");
                var hora = f.HoraInicio.ToString(@"hh\:mm");
                var estado = EscapeCsv(f.Estado);
                var origen = EscapeCsv(f.Origen ?? "Ausente");
                var creado = f.CreadoEn.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

                await writer.WriteLineAsync($"{i + 1},{codigo},{nombre},{correo},{fecha},{hora},{estado},{origen},{creado}");
            }

            await writer.FlushAsync();
        }

        return memoryStream.ToArray();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
