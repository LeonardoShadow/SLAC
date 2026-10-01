using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Core.Security;
using SLAC.Features.Attendance.Services;

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
            [FromQuery(Name = "modo")] string? modo,
            [FromQuery(Name = "r")] string? rStr,
            [FromQuery(Name = "clat")] string? clatStr,
            [FromQuery(Name = "clon")] string? clonStr,
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

            int? r = int.TryParse(rStr, out var rVal) ? rVal : null;
            double? clat = double.TryParse(clatStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cvLat) ? cvLat : null;
            double? clon = double.TryParse(clonStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cvLon) ? cvLon : null;

            var deviceCookie = httpContext.Request.Cookies[CookieDeviceName];
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();
            var esModoWifi = string.Equals(modo, "wifi", StringComparison.OrdinalIgnoreCase);

            // Pre-validar el QR antes de mostrar cualquier vista
            if (!tokenService.TryValidateSessionQrToken(t, out var sessionToken, out var errorMsg, rotacionTolerancia: 4, validarRotacion: true) || sessionToken == null)
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

            // MODO WI-FI DIRECTO: Si el docente activó modo Wi-Fi, procesar inmediatamente sin solicitar GPS
            if (esModoWifi)
            {
                if (!string.IsNullOrWhiteSpace(deviceCookie))
                {
                    var scanReq = new AttendanceScanRequest
                    {
                        SesionId = sesionId,
                        QrToken = t,
                        DeviceToken = deviceCookie,
                        UserAgent = userAgent,
                        Modo = "wifi"
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

                    if (scanRes.RequiereRevinculacion)
                    {
                        var revHtml = AttendanceStudentView.RenderRevinculacionPendingView(
                            scanRes.EstudianteNombre,
                            scanRes.EstudianteCodigo,
                            scanRes.MateriaNombre,
                            scanRes.MensajeRevinculacion,
                            scanRes.SolicitudRevinculacionId,
                            sesionId);
                        return Results.Content(revHtml, "text/html");
                    }

                    if (scanRes.RequiereRegistro)
                    {
                        var formHtml = AttendanceStudentView.RenderRegistrationView(sesionId, t, scanRes.MateriaNombre ?? "Clase", scanRes.MateriaCodigo ?? "", modo: "wifi", radioTolerancia: r, latReferencia: clat, lonReferencia: clon);
                        return Results.Content(formHtml, "text/html");
                    }

                    var errHtml = AttendanceStudentView.RenderErrorView("No se pudo registrar asistencia", scanRes.MensajeError ?? "Código QR inválido o expirado.");
                    return Results.Content(errHtml, "text/html");
                }

                // Primer escaneo en modo Wi-Fi
                var regWifiHtml = AttendanceStudentView.RenderRegistrationView(sesionId, t, materiaNombre, materiaCodigo, modo: "wifi", radioTolerancia: r, latReferencia: clat, lonReferencia: clon);
                return Results.Content(regWifiHtml, "text/html");
            }

            // MODO GPS OBLIGATORIO:
            // Si el alumno ya cuenta con credencial de dispositivo, validar presencia física por GPS en aula
            if (!string.IsNullOrWhiteSpace(deviceCookie))
            {
                var gpsView = AttendanceStudentView.RenderGpsAutoVerifyView(sesionId, t, materiaNombre, materiaCodigo, r, clat, clon);
                return Results.Content(gpsView, "text/html");
            }

            // Primer escaneo (Sin cookie de dispositivo): Mostrar formulario de vinculación con GPS
            var regHtml = AttendanceStudentView.RenderRegistrationView(sesionId, t, materiaNombre, materiaCodigo, modo: "gps", radioTolerancia: r, latReferencia: clat, lonReferencia: clon);
            return Results.Content(regHtml, "text/html");
        });

        // POST /a/{sesionId}/confirmar-gps (Verificación automática de coordenadas para dispositivos ya vinculados)
        endpoints.MapPost("/a/{sesionId:guid}/confirmar-gps", async (
            Guid sesionId,
            IFormCollection form,
            HttpContext httpContext,
            ISuscripcionService suscripcionService,
            CancellationToken ct) =>
        {
            var t = form["t"].ToString().Trim();
            var deviceCookie = httpContext.Request.Cookies[CookieDeviceName];
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            if (string.IsNullOrWhiteSpace(t))
            {
                var html = AttendanceStudentView.RenderErrorView("Solicitud Inválida", "Falta el token de sesión QR.");
                return Results.Content(html, "text/html");
            }

            double? latitud = double.TryParse(form["lat"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var latVal) ? latVal : null;
            double? longitud = double.TryParse(form["lon"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lonVal) ? lonVal : null;
            double? precision = double.TryParse(form["acc"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var accVal) ? accVal : null;
            int? radioPersonalizado = int.TryParse(form["r"].ToString(), out var rVal) ? rVal : null;
            double? clat = double.TryParse(form["clat"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var clatVal) ? clatVal : null;
            double? clon = double.TryParse(form["clon"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var clonVal) ? clonVal : null;

            var modo = form["modo"].ToString().Trim();
            var scanReq = new AttendanceScanRequest
            {
                SesionId = sesionId,
                QrToken = t,
                DeviceToken = deviceCookie,
                UserAgent = userAgent,
                Latitud = latitud,
                Longitud = longitud,
                PrecisionGpsMetros = precision,
                RadioToleranciaPersonalizado = radioPersonalizado,
                LatitudReferencia = clat,
                LongitudReferencia = clon,
                Modo = !string.IsNullOrWhiteSpace(modo) ? modo : "gps"
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

            if (scanRes.RequiereRevinculacion)
            {
                var revHtml = AttendanceStudentView.RenderRevinculacionPendingView(
                    scanRes.EstudianteNombre,
                    scanRes.EstudianteCodigo,
                    scanRes.MateriaNombre,
                    scanRes.MensajeRevinculacion,
                    scanRes.SolicitudRevinculacionId,
                    sesionId);
                return Results.Content(revHtml, "text/html");
            }

            if (scanRes.RequiereRegistro)
            {
                var formHtml = AttendanceStudentView.RenderRegistrationView(sesionId, t, scanRes.MateriaNombre ?? "Clase", scanRes.MateriaCodigo ?? "", modo: "gps", radioTolerancia: radioPersonalizado, latReferencia: clat, lonReferencia: clon);
                return Results.Content(formHtml, "text/html");
            }

            var errHtml = AttendanceStudentView.RenderErrorView("No se pudo registrar asistencia", scanRes.MensajeError ?? "Código QR inválido o expirado.");
            return Results.Content(errHtml, "text/html");
        }).DisableAntiforgery();

        // POST /a/{sesionId} (Pasos 32 y 33: Procesamiento del formulario y emisión de cookie)
        endpoints.MapPost("/a/{sesionId:guid}", async (
            Guid sesionId,
            IFormCollection form,
            HttpContext httpContext,
            ISuscripcionService suscripcionService,
            CancellationToken ct) =>
        {
            var t = form["t"].ToString().Trim();
            var identificador = form["identificador"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(identificador))
            {
                identificador = !string.IsNullOrWhiteSpace(form["codigo"]) ? form["codigo"].ToString().Trim() :
                                !string.IsNullOrWhiteSpace(form["correo"]) ? form["correo"].ToString().Trim() :
                                form["ci"].ToString().Trim();
            }

            var esCorreo = identificador.Contains('@');
            var correo = esCorreo ? identificador : form["correo"].ToString().Trim();
            var codigo = esCorreo ? "" : identificador;
            var docIdentidad = esCorreo ? "" : (!string.IsNullOrWhiteSpace(form["ci"]) ? form["ci"].ToString().Trim() : identificador);

            var nombres = form["nombres"].ToString().Trim();
            var apellidos = form["apellidos"].ToString().Trim();
            var aceptaTerminos = form["aceptaTerminos"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();
            var modo = !string.IsNullOrWhiteSpace(form["modo"]) ? form["modo"].ToString().Trim() : "gps";

            double? latitud = double.TryParse(form["lat"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var latForm) ? latForm : null;
            double? longitud = double.TryParse(form["lon"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lonForm) ? lonForm : null;
            double? precision = double.TryParse(form["acc"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var accForm) ? accForm : null;
            int? radioPersonalizado = int.TryParse(form["r"].ToString(), out var rVal) ? rVal : null;
            double? clat = double.TryParse(form["clat"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var clatVal) ? clatVal : null;
            double? clon = double.TryParse(form["clon"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var clonVal) ? clonVal : null;

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
                Identificador = identificador,
                DocumentoIdentidad = docIdentidad,
                Codigo = codigo,
                Nombres = nombres,
                Apellidos = apellidos,
                Correo = correo,
                AceptaTerminos = aceptaTerminos,
                UserAgent = userAgent,
                Modo = modo,
                Latitud = latitud,
                Longitud = longitud,
                PrecisionGpsMetros = precision,
                RadioToleranciaPersonalizado = radioPersonalizado,
                LatitudReferencia = clat,
                LongitudReferencia = clon
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

            if (scanRes.RequiereRevinculacion)
            {
                var revHtml = AttendanceStudentView.RenderRevinculacionPendingView(
                    scanRes.EstudianteNombre,
                    scanRes.EstudianteCodigo,
                    scanRes.MateriaNombre,
                    scanRes.MensajeRevinculacion,
                    scanRes.SolicitudRevinculacionId,
                    sesionId);
                return Results.Content(revHtml, "text/html");
            }

            // Si falló por validación de formulario o GPS, volver a mostrar el formulario con el error
            var formRetryHtml = AttendanceStudentView.RenderRegistrationView(
                sesionId,
                t,
                scanRes.MateriaNombre ?? "Clase",
                scanRes.MateriaCodigo ?? "",
                scanRes.MensajeError ?? "Revisa los datos de identificación ingresados.",
                modo: modo,
                radioTolerancia: radioPersonalizado,
                latReferencia: clat,
                lonReferencia: clon);

            return Results.Content(formRetryHtml, "text/html");
        }).DisableAntiforgery(); // Enlace público estático para escaneo QR móvil

        // GET /a/revinculacion-status/{solicitudId:guid} (Polling móvil de autorización de revinculación RN-05)
        endpoints.MapGet("/a/revinculacion-status/{solicitudId:guid}", async (
            Guid solicitudId,
            HttpContext httpContext,
            IRevinculacionRepository revinculacionRepo,
            IDispositivoRepository dispositivoRepo,
            IEstudianteRepository estudianteRepo,
            IListaAsistenciaRepository listaRepo,
            IAsistenciaDetalleRepository detalleRepo,
            ITokenService tokenService,
            IClassroomNotificationService notificationService,
            CancellationToken ct) =>
        {
            var solicitud = await revinculacionRepo.ObtenerPorIdAsync(solicitudId, ct);
            if (solicitud == null)
            {
                return Results.Json(new { estado = "no_encontrada" });
            }

            if (!solicitud.UsadaEn.HasValue)
            {
                return Results.Json(new { estado = "pendiente" });
            }

            // Ya fue autorizada por el docente en el aula: Emitir credencial de dispositivo a este teléfono
            var nuevoDispId = Guid.NewGuid();
            var (tokenString, jtiHash) = tokenService.GenerateDeviceCredential(nuevoDispId, solicitud.InstitucionId);

            var nuevoDisp = new Dispositivo
            {
                Id = nuevoDispId,
                JtiHash = jtiHash,
                Kid = "slac-key-v1",
                AgenteResumen = httpContext.Request.Headers.UserAgent.ToString()
            };

            await dispositivoRepo.RegistrarDispositivoAsync(nuevoDisp, solicitud.EstudianteId, solicitud.InstitucionId, ct);
            AppendDeviceCookie(httpContext, tokenString);

            // Registrar asistencia inmediatamente si se provee sesión
            var sesionIdStr = httpContext.Request.Query["sesionId"].ToString();
            if (Guid.TryParse(sesionIdStr, out var sesionId))
            {
                var lista = await listaRepo.ObtenerPorIdAsync(sesionId, ct);
                if (lista != null)
                {
                    var est = await estudianteRepo.ObtenerPorIdAsync(solicitud.EstudianteId, ct);
                    if (est != null)
                    {
                        var existente = await detalleRepo.ObtenerPorListaYEstudianteAsync(lista.Id, est.Id, ct);
                        if (existente == null)
                        {
                            var detalle = new AsistenciaDetalle
                            {
                                Id = Guid.NewGuid(),
                                InstitucionId = lista.InstitucionId,
                                ListaId = lista.Id,
                                EstudianteId = est.Id,
                                Estado = "Presente",
                                Origen = "QR",
                                DispositivoId = nuevoDisp.Id,
                                HoraLlegada = DateTimeOffset.UtcNow,
                                MinutosDesdeInicio = (int)Math.Max(0, (DateTime.Now.TimeOfDay - lista.HoraInicio).TotalMinutes)
                            };
                            await detalleRepo.RegistrarAsistenciaAsync(detalle, ct);
                        }

                        var detList = await detalleRepo.ListarPorListaAsync(lista.Id, ct);
                        var totalPres = Math.Max(1, detList.Count(x => x.Estado == "Presente"));
                        await notificationService.NotifyAttendanceRecordedAsync(lista.Id, totalPres, est.NombreCompleto, est.Codigo, ct);
                    }
                }
            }

            return Results.Json(new { estado = "aprobada", recargar = true });
        }).DisableAntiforgery();

        return endpoints;
    }

    private static void AppendDeviceCookie(HttpContext httpContext, string token)
    {
        httpContext.Response.Cookies.Append(CookieDeviceName, token, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });
    }
}
