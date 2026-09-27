using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Security;

namespace SLAC.Features.Attendance.Hubs;

public static class AttendanceStudentEndpoints
{
    private const string CookieDeviceName = "slac_device";

    public static IEndpointRouteBuilder MapAttendanceStudentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /a/{sesionId}?t={token} (Paso 31: Validación de entrada y detección de dispositivo)
        endpoints.MapGet("/a/{sesionId:guid}", async (
            Guid sesionId,
            [FromQuery(Name = "t")] string? t,
            HttpContext httpContext,
            ISuscripcionService suscripcionService,
            ITokenService tokenService,
            IListaAsistenciaRepository listaRepo,
            IMateriaRepository materiaRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(t))
            {
                var html = AttendanceStudentView.RenderErrorView("Enlace Incompleto", "El enlace no contiene el token de verificación del código QR.");
                return Results.Content(html, "text/html");
            }

            var deviceCookie = httpContext.Request.Cookies[CookieDeviceName];
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            // Si el alumno ya cuenta con credencial de dispositivo, procesar el escaneo directamente
            if (!string.IsNullOrWhiteSpace(deviceCookie))
            {
                var scanReq = new AttendanceScanRequest
                {
                    SesionId = sesionId,
                    QrToken = t,
                    DeviceToken = deviceCookie,
                    UserAgent = userAgent
                };

                var scanRes = await suscripcionService.ProcesarEscaneoAsync(scanReq, ct);
                if (scanRes.Exito)
                {
                    if (!string.IsNullOrWhiteSpace(scanRes.NuevaDeviceCookie))
                    {
                        AppendDeviceCookie(httpContext, scanRes.NuevaDeviceCookie);
                    }

                    var successHtml = AttendanceStudentView.RenderSuccessView(scanRes);
                    return Results.Content(successHtml, "text/html");
                }

                if (scanRes.RequiereRegistro)
                {
                    var formHtml = AttendanceStudentView.RenderRegistrationView(sesionId, t, scanRes.MateriaNombre ?? "Clase", scanRes.MateriaCodigo ?? "");
                    return Results.Content(formHtml, "text/html");
                }

                var errHtml = AttendanceStudentView.RenderErrorView("No se pudo registrar asistencia", scanRes.MensajeError ?? "Código QR inválido o expirado.");
                return Results.Content(errHtml, "text/html");
            }

            // Primer escaneo (Sin cookie de dispositivo): Pre-validar el QR y mostrar formulario
            if (!tokenService.TryValidateSessionQrToken(t, out var sessionToken, out var errorMsg) || sessionToken == null)
            {
                var invalidTokenHtml = AttendanceStudentView.RenderErrorView("Código QR Inválido o Expirado", errorMsg ?? "El código QR ha expirado. Escanea el código vigente en la pantalla del aula.");
                return Results.Content(invalidTokenHtml, "text/html");
            }

            if (sessionToken.SesionId != sesionId)
            {
                var sessionMismatchHtml = AttendanceStudentView.RenderErrorView("Sesión Incorrecta", "El código QR no corresponde a la sesión de clase actual.");
                return Results.Content(sessionMismatchHtml, "text/html");
            }

            var lista = await listaRepo.ObtenerPorIdAsync(sesionId, ct);
            var materia = lista != null ? await materiaRepo.ObtenerPorIdAsync(lista.MateriaId, ct) : null;
            var materiaNombre = materia?.Nombre ?? "Materia";
            var materiaCodigo = materia?.Codigo ?? "";

            var regHtml = AttendanceStudentView.RenderRegistrationView(sesionId, t, materiaNombre, materiaCodigo);
            return Results.Content(regHtml, "text/html");
        });

        // POST /a/{sesionId} (Pasos 32 y 33: Procesamiento del formulario y emisión de cookie)
        endpoints.MapPost("/a/{sesionId:guid}", async (
            Guid sesionId,
            IFormCollection form,
            HttpContext httpContext,
            ISuscripcionService suscripcionService,
            CancellationToken ct) =>
        {
            var t = form["t"].ToString();
            var codigo = form["codigo"].ToString();
            var nombres = form["nombres"].ToString();
            var apellidos = form["apellidos"].ToString();
            var correo = form["correo"].ToString();
            var aceptaTerminos = form["aceptaTerminos"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            if (string.IsNullOrWhiteSpace(t))
            {
                var html = AttendanceStudentView.RenderErrorView("Solicitud Inválida", "Falta el token de sesión QR.");
                return Results.Content(html, "text/html");
            }

            var scanReq = new AttendanceScanRequest
            {
                SesionId = sesionId,
                QrToken = t,
                DeviceToken = null,
                Codigo = codigo,
                Nombres = nombres,
                Apellidos = apellidos,
                Correo = correo,
                AceptaTerminos = aceptaTerminos,
                UserAgent = userAgent
            };

            var scanRes = await suscripcionService.ProcesarEscaneoAsync(scanReq, ct);

            if (scanRes.Exito)
            {
                // Inyectar cookie HttpOnly de dispositivo (Paso 33)
                if (!string.IsNullOrWhiteSpace(scanRes.NuevaDeviceCookie))
                {
                    AppendDeviceCookie(httpContext, scanRes.NuevaDeviceCookie);
                }

                var successHtml = AttendanceStudentView.RenderSuccessView(scanRes);
                return Results.Content(successHtml, "text/html");
            }

            // Si falló por validación de formulario, volver a mostrar el formulario con el error
            var formRetryHtml = AttendanceStudentView.RenderRegistrationView(
                sesionId,
                t,
                scanRes.MateriaNombre ?? "Clase",
                scanRes.MateriaCodigo ?? "",
                scanRes.MensajeError ?? "Revisa los campos obligatorios.");

            return Results.Content(formRetryHtml, "text/html");
        }).DisableAntiforgery(); // Enlace público estático para escaneo QR móvil

        return endpoints;
    }

    private static void AppendDeviceCookie(HttpContext httpContext, string token)
    {
        httpContext.Response.Cookies.Append(CookieDeviceName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
    }
}
