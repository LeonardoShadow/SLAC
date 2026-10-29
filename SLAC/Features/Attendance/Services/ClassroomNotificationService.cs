using Microsoft.AspNetCore.SignalR;
using SLAC.Features.Attendance.Hubs;

namespace SLAC.Features.Attendance.Services;

public class AsistenciaTelemetriaDto
{
    public string Nombre { get; set; } = "";
    public string Codigo { get; set; } = "";
    public int TotalPresentes { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public double? PrecisionMetros { get; set; }
    public double? DistanciaMetros { get; set; }
    public double? QrLatitud { get; set; }
    public double? QrLongitud { get; set; }
    public double? RadioPermitido { get; set; }
    public string Modo { get; set; } = "gps";
}

public interface IClassroomNotificationService
{
    Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string? estudianteCodigo, CancellationToken cancellationToken = default);
    Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string estudianteNombre, string? estudianteCodigo, CancellationToken cancellationToken = default);
    Task NotifyAttendanceRecordedAsync(
        Guid sesionId,
        int totalPresentes,
        string estudianteNombre,
        string? estudianteCodigo,
        double? latitud,
        double? longitud,
        double? precisionMetros,
        double? distanciaMetros,
        double? qrLatitud,
        double? qrLongitud,
        double? radioPermitido,
        string? modo,
        CancellationToken cancellationToken = default)
        => NotifyAttendanceRecordedAsync(sesionId, totalPresentes, estudianteNombre, estudianteCodigo, cancellationToken);
    Task NotifyQrRotatedAsync(Guid sesionId, string nuevoToken, int rotacionIndex, CancellationToken cancellationToken = default);
    Task NotifySessionClosedAsync(Guid sesionId, int totalPresentes, int totalFaltas, CancellationToken cancellationToken = default);
}

public class ClassroomNotificationService(IHubContext<AttendanceHub> hubContext) : IClassroomNotificationService
{
    private readonly IHubContext<AttendanceHub> _hubContext = hubContext;

    public Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string? estudianteCodigo, CancellationToken cancellationToken = default)
        => NotifyAttendanceRecordedAsync(sesionId, totalPresentes, estudianteCodigo ?? "Estudiante", estudianteCodigo, cancellationToken);

    public async Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string estudianteNombre, string? estudianteCodigo, CancellationToken cancellationToken = default)
    {
        var group = AttendanceHub.GetGroupName(sesionId.ToString());
        await _hubContext.Clients.Group(group).SendAsync("AsistenciaRegistrada", estudianteNombre, estudianteCodigo ?? "", totalPresentes, cancellationToken);
        await _hubContext.Clients.Group(group).SendAsync("AsistenciaActualizada", totalPresentes, estudianteCodigo ?? "", cancellationToken);
    }

    public async Task NotifyAttendanceRecordedAsync(
        Guid sesionId,
        int totalPresentes,
        string estudianteNombre,
        string? estudianteCodigo,
        double? latitud,
        double? longitud,
        double? precisionMetros,
        double? distanciaMetros,
        double? qrLatitud,
        double? qrLongitud,
        double? radioPermitido,
        string? modo,
        CancellationToken cancellationToken = default)
    {
        var group = AttendanceHub.GetGroupName(sesionId.ToString());
        await _hubContext.Clients.Group(group).SendAsync("AsistenciaRegistrada", estudianteNombre, estudianteCodigo ?? "", totalPresentes, cancellationToken);
        
        var dto = new AsistenciaTelemetriaDto
        {
            Nombre = estudianteNombre,
            Codigo = estudianteCodigo ?? "",
            TotalPresentes = totalPresentes,
            Latitud = latitud,
            Longitud = longitud,
            PrecisionMetros = precisionMetros,
            DistanciaMetros = distanciaMetros,
            QrLatitud = qrLatitud,
            QrLongitud = qrLongitud,
            RadioPermitido = radioPermitido,
            Modo = modo ?? "gps"
        };
        await _hubContext.Clients.Group(group).SendAsync("AsistenciaRegistradaConTelemetria", dto, cancellationToken);
        await _hubContext.Clients.Group(group).SendAsync("AsistenciaActualizada", totalPresentes, estudianteCodigo ?? "", cancellationToken);
    }

    public async Task NotifyQrRotatedAsync(Guid sesionId, string nuevoToken, int rotacionIndex, CancellationToken cancellationToken = default)
    {
        var group = AttendanceHub.GetGroupName(sesionId.ToString());
        await _hubContext.Clients.Group(group).SendAsync("QrRotado", nuevoToken, rotacionIndex, cancellationToken);
    }

    public async Task NotifySessionClosedAsync(Guid sesionId, int totalPresentes, int totalFaltas, CancellationToken cancellationToken = default)
    {
        var group = AttendanceHub.GetGroupName(sesionId.ToString());
        await _hubContext.Clients.Group(group).SendAsync("SesionCerrada", totalPresentes, totalFaltas, cancellationToken);
    }
}
