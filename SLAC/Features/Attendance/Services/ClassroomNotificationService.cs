using Microsoft.AspNetCore.SignalR;
using SLAC.Features.Attendance.Hubs;

namespace SLAC.Features.Attendance.Services;

public interface IClassroomNotificationService
{
    Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string? estudianteCodigo, CancellationToken cancellationToken = default);
    Task NotifyQrRotatedAsync(Guid sesionId, string nuevoToken, int rotacionIndex, CancellationToken cancellationToken = default);
    Task NotifySessionClosedAsync(Guid sesionId, int totalPresentes, int totalFaltas, CancellationToken cancellationToken = default);
}

public class ClassroomNotificationService(IHubContext<AttendanceHub> hubContext) : IClassroomNotificationService
{
    private readonly IHubContext<AttendanceHub> _hubContext = hubContext;

    public async Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string? estudianteCodigo, CancellationToken cancellationToken = default)
    {
        var group = AttendanceHub.GetGroupName(sesionId.ToString());
        await _hubContext.Clients.Group(group).SendAsync("AsistenciaActualizada", totalPresentes, estudianteCodigo, cancellationToken);
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
