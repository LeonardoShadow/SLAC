using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace SLAC.Features.Attendance.Hubs;

/// <summary>
/// Hub de SignalR para la pantalla de proyección del aula (Docente).
/// Recibe eventos en tiempo real disparados vía Redis Pub/Sub (SRS 4.1 y 4.7).
/// </summary>
public class AttendanceHub(ILogger<AttendanceHub> logger) : Hub
{
    private readonly ILogger<AttendanceHub> _logger = logger;

    /// <summary>
    /// Permite al docente unirse a la sala de proyección en vivo de su sesión de clase.
    /// </summary>
    public async Task JoinClassroomSession(string sesionId)
    {
        var groupName = GetGroupName(sesionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Cliente {ConnectionId} se unió a la proyección de la sesión {SesionId}", Context.ConnectionId, sesionId);
    }

    /// <summary>
    /// Desconecta al cliente de la sala de proyección de la clase.
    /// </summary>
    public async Task LeaveClassroomSession(string sesionId)
    {
        var groupName = GetGroupName(sesionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Cliente {ConnectionId} salió de la proyección de la sesión {SesionId}", Context.ConnectionId, sesionId);
    }

    public static string GetGroupName(string sesionId) => $"classroom_session_{sesionId}";
}

public static class AttendanceHubExtensions
{
    public static WebApplication MapAttendanceStudentEndpoints(this WebApplication app)
    {
        AttendanceStudentEndpoints.MapAttendanceStudentEndpoints(app);
        return app;
    }
}
