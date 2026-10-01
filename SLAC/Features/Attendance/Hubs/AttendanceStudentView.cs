using SLAC.Core.Attendance.Services;
using SLAC.Core;

namespace SLAC.Features.Attendance.Hubs;

/// <summary>
/// Generador de vistas HTML para el Flujo del Estudiante (SSR estático puro sin WebSockets - Pasos 31, 32 y 33).
/// Diseñado para máxima velocidad, accesibilidad móvil y estética premium (Inter font, dark mode, glassmorphism).
/// </summary>
public static class AttendanceStudentView
{
    public static string RenderSuccessView(AttendanceScanResult result)
    {
        var estudiante = EncodeHtml(result.EstudianteNombre ?? "Estudiante");
        var codigo = EncodeHtml(result.EstudianteCodigo ?? "");
        var materia = EncodeHtml(result.MateriaNombre ?? "Clase");
        var codigoMateria = EncodeHtml(result.MateriaCodigo ?? "");
        var horaLlegada = TimeHelper.FormatearHoraLocal(result.HoraLlegada);
        var minutos = result.MinutosDesdeInicio ?? 0;
        var estadoBadge = result.YaRegistradoHoy ? "Asistencia Previamente Registrada" : "¡Asistencia a Clases Registrada!";
        var tiempoTexto = minutos == 0 ? "A tiempo al inicio" : $"{minutos} min tras apertura";

        return $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Asistencia Confirmada | SLAC</title>
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" rel="stylesheet">
            <style>
                :root {
                    --bg-dark: #090d16;
                    --card-bg: rgba(17, 24, 39, 0.85);
                    --border: rgba(255, 255, 255, 0.08);
                    --primary: #4f46e5;
                    --success: #10b981;
                    --text-main: #f8fafc;
                    --text-muted: #94a3b8;
                }
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: 'Plus Jakarta Sans', sans-serif; }
                body {
                    background: radial-gradient(circle at top center, #1e1b4b 0%, var(--bg-dark) 70%);
                    color: var(--text-main);
                    min-height: 100vh;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    padding: 1.25rem;
                }
                .card {
                    background: var(--card-bg);
                    backdrop-filter: blur(16px);
                    border: 1px solid var(--border);
                    border-radius: 1.5rem;
                    padding: 2.25rem 1.75rem;
                    width: 100%;
                    max-width: 440px;
                    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5), 0 0 40px -10px rgba(16, 185, 129, 0.2);
                    text-align: center;
                    animation: slideUp 0.4s ease-out;
                }
                @keyframes slideUp { from { opacity: 0; transform: translateY(16px); } to { opacity: 1; transform: translateY(0); } }
                .icon-circle {
                    width: 76px;
                    height: 76px;
                    background: rgba(16, 185, 129, 0.15);
                    border: 2px solid rgba(16, 185, 129, 0.4);
                    border-radius: 50%;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    margin: 0 auto 1.5rem auto;
                    color: var(--success);
                }
                .badge {
                    display: inline-block;
                    background: rgba(16, 185, 129, 0.2);
                    color: #34d399;
                    font-size: 0.8125rem;
                    font-weight: 700;
                    padding: 0.35rem 0.85rem;
                    border-radius: 9999px;
                    letter-spacing: 0.05em;
                    text-transform: uppercase;
                    margin-bottom: 0.75rem;
                    border: 1px solid rgba(16, 185, 129, 0.3);
                }
                h1 { font-size: 1.5rem; font-weight: 800; margin-bottom: 0.5rem; line-height: 1.2; }
                p.sub { color: var(--text-muted); font-size: 0.9375rem; margin-bottom: 1.5rem; }
                .details-box {
                    background: rgba(255, 255, 255, 0.03);
                    border: 1px solid rgba(255, 255, 255, 0.06);
                    border-radius: 1rem;
                    padding: 1.25rem;
                    text-align: left;
                    margin-bottom: 1.5rem;
                }
                .detail-row {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    padding: 0.5rem 0;
                    font-size: 0.875rem;
                    border-bottom: 1px solid rgba(255, 255, 255, 0.05);
                }
                .detail-row:last-child { border-bottom: none; }
                .detail-label { color: var(--text-muted); font-weight: 500; }
                .detail-val { color: var(--text-main); font-weight: 700; text-align: right; }
                .footer-notice {
                    font-size: 0.75rem;
                    color: #64748b;
                    line-height: 1.4;
                }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="icon-circle">
                    <svg width="38" height="38" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                        <polyline points="20 6 9 17 4 12"></polyline>
                    </svg>
                </div>
                <div class="badge">{{estadoBadge}}</div>
                <h1>{{estudiante}}</h1>
                <p class="sub">{{codigo}} &bull; {{materia}} ({{codigoMateria}})</p>

                <div class="details-box">
                    <div class="detail-row">
                        <span class="detail-label">Hora Registrada</span>
                        <span class="detail-val">{{horaLlegada}}</span>
                    </div>
                    <div class="detail-row">
                        <span class="detail-label">Estado</span>
                        <span class="detail-val" style="color: #34d399;">Presente</span>
                    </div>
                    <div class="detail-row">
                        <span class="detail-label">Puntualidad</span>
                        <span class="detail-val">{{tiempoTexto}}</span>
                    </div>
                    <div class="detail-row">
                        <span class="detail-label">Credencial</span>
                        <span class="detail-val" style="color: #818cf8;">Dispositivo Verificado (ES256)</span>
                    </div>
                </div>

                <p class="footer-notice">
                    Tu asistencia ha sido verificada y transmitida a la lista oficial del docente. Ya puedes cerrar esta ventana.
                </p>
            </div>
        </body>
        </html>
        """;
    }

    public static string RenderRegistrationView(
        Guid sesionId,
        string qrToken,
        string materiaNombre,
        string materiaCodigo,
        string? errorMensaje = null,
        string modo = "gps",
        int? radioTolerancia = null,
        double? latReferencia = null,
        double? lonReferencia = null)
    {
        var tokenSeguro = EncodeHtml(qrToken);
        var latReferenciaString = latReferencia?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var lonReferenciaString = lonReferencia?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var materia = EncodeHtml(materiaNombre);
        var codigoMat = EncodeHtml(materiaCodigo);
        var esWifi = string.Equals(modo, "wifi", StringComparison.OrdinalIgnoreCase);
        var bannerGpsHtml = esWifi
            ? """
              <div id="gps-status" style="display: flex; align-items: center; gap: 0.5rem; background: rgba(16, 185, 129, 0.15); border: 1px solid rgba(16, 185, 129, 0.3); border-radius: 0.75rem; padding: 0.65rem 0.85rem; font-size: 0.8125rem; color: #34d399; margin-bottom: 1.25rem;">
                  <span>📶 <b>Modo Red Wi-Fi Aula:</b> Conexión autorizada directamente por código de sesión.</span>
              </div>
              """
            : """
              <div id="gps-status" style="display: flex; align-items: center; gap: 0.5rem; background: rgba(59, 130, 246, 0.1); border: 1px solid rgba(59, 130, 246, 0.25); border-radius: 0.75rem; padding: 0.65rem 0.85rem; font-size: 0.8125rem; color: #93c5fd; margin-bottom: 1.25rem;">
                  <span>📡 Calibrando ubicación GPS para confirmar asistencia en aula...</span>
              </div>
              """;
        var textoBoton = esWifi ? "📱 Vincular Teléfono y Marcar Asistencia" : "📍 Vincular Teléfono y Marcar Asistencia";
        var errorHtml = string.IsNullOrWhiteSpace(errorMensaje)
            ? ""
            : $$"""
            <div class="error-banner">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
                <span>{{EncodeHtml(errorMensaje)}}</span>
            </div>
            """;

        return $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Registro de Asistencia | SLAC</title>
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" rel="stylesheet">
            <style>
                :root {
                    --bg-dark: #090d16;
                    --card-bg: rgba(17, 24, 39, 0.9);
                    --border: rgba(255, 255, 255, 0.1);
                    --primary: #4f46e5;
                    --primary-hover: #4338ca;
                    --text-main: #f8fafc;
                    --text-muted: #94a3b8;
                    --danger: #ef4444;
                }
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: 'Plus Jakarta Sans', sans-serif; }
                body {
                    background: radial-gradient(circle at top center, #1e1b4b 0%, var(--bg-dark) 75%);
                    color: var(--text-main);
                    min-height: 100vh;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    padding: 1.25rem;
                }
                .card {
                    background: var(--card-bg);
                    backdrop-filter: blur(16px);
                    border: 1px solid var(--border);
                    border-radius: 1.5rem;
                    padding: 2rem 1.75rem;
                    width: 100%;
                    max-width: 440px;
                    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.6);
                    animation: fadeIn 0.3s ease-out;
                }
                @keyframes fadeIn { from { opacity: 0; transform: scale(0.98); } to { opacity: 1; transform: scale(1); } }
                .header-tag {
                    display: inline-block;
                    background: rgba(99, 102, 241, 0.15);
                    color: #818cf8;
                    font-size: 0.75rem;
                    font-weight: 700;
                    padding: 0.3rem 0.75rem;
                    border-radius: 9999px;
                    letter-spacing: 0.05em;
                    text-transform: uppercase;
                    margin-bottom: 0.5rem;
                    border: 1px solid rgba(99, 102, 241, 0.3);
                }
                h1 { font-size: 1.375rem; font-weight: 800; margin-bottom: 0.25rem; }
                p.desc { font-size: 0.875rem; color: var(--text-muted); margin-bottom: 1.5rem; }
                .error-banner {
                    background: rgba(239, 68, 68, 0.15);
                    border: 1px solid rgba(239, 68, 68, 0.3);
                    color: #fca5a5;
                    padding: 0.75rem 1rem;
                    border-radius: 0.75rem;
                    font-size: 0.8125rem;
                    display: flex;
                    align-items: center;
                    gap: 0.625rem;
                    margin-bottom: 1.25rem;
                }
                .form-group { margin-bottom: 1.125rem; }
                label { display: block; font-size: 0.8125rem; font-weight: 600; color: #cbd5e1; margin-bottom: 0.375rem; }
                input[type="text"], input[type="email"] {
                    width: 100%;
                    background: rgba(15, 23, 42, 0.6);
                    border: 1px solid rgba(255, 255, 255, 0.12);
                    border-radius: 0.75rem;
                    padding: 0.75rem 1rem;
                    color: #fff;
                    font-size: 0.9375rem;
                    outline: none;
                    transition: border-color 0.2s, box-shadow 0.2s;
                }
                input:focus { border-color: var(--primary); box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.25); }
                .terms-box {
                    display: flex;
                    align-items: flex-start;
                    gap: 0.75rem;
                    margin: 1.25rem 0 1.5rem 0;
                    padding: 0.875rem;
                    background: rgba(255, 255, 255, 0.02);
                    border-radius: 0.75rem;
                    border: 1px solid rgba(255, 255, 255, 0.05);
                }
                .terms-box input[type="checkbox"] {
                    margin-top: 0.2rem;
                    width: 1.125rem;
                    height: 1.125rem;
                    accent-color: var(--primary);
                    cursor: pointer;
                }
                .terms-text {
                    font-size: 0.75rem;
                    color: var(--text-muted);
                    line-height: 1.4;
                }
                button.submit-btn {
                    width: 100%;
                    background: var(--primary);
                    color: #fff;
                    font-size: 1rem;
                    font-weight: 700;
                    padding: 0.875rem;
                    border: none;
                    border-radius: 0.75rem;
                    cursor: pointer;
                    transition: background 0.2s, transform 0.1s;
                    box-shadow: 0 4px 12px rgba(79, 70, 229, 0.4);
                }
                button.submit-btn:hover { background: var(--primary-hover); }
                button.submit-btn:active { transform: scale(0.99); }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="header-tag">{{codigoMat}}</div>
                <h1>{{materia}}</h1>
                <p class="desc">Primer escaneo detectado. Vincula este teléfono móvil a tu identidad estudiantil para registrar tu asistencia de hoy.</p>

                {{errorHtml}}

                {{bannerGpsHtml}}

                <form method="POST" action="/a/{{sesionId}}" id="regForm">
                    <input type="hidden" name="t" value="{{tokenSeguro}}" />
                    <input type="hidden" name="modo" value="{{modo}}" />
                    <input type="hidden" name="r" value="{{radioTolerancia}}" />
                    <input type="hidden" name="clat" value="{{latReferenciaString}}" />
                    <input type="hidden" name="clon" value="{{lonReferenciaString}}" />
                    <input type="hidden" id="lat" name="lat" />
                    <input type="hidden" id="lon" name="lon" />
                    <input type="hidden" id="acc" name="acc" />

                    <div class="form-group">
                        <label for="identificador">Código de Estudiante o Correo Institucional (CI) *</label>
                        <input type="text" id="identificador" name="identificador" required autofocus />
                        <span style="font-size: 0.75rem; color: #94a3b8; display: block; margin-top: 0.25rem;">
                            Ingresa tu código único, carnet (CI) o correo institucional.
                        </span>
                    </div>

                    <div class="terms-box">
                        <input type="checkbox" id="acepta" name="aceptaTerminos" value="true" required checked />
                        <label for="acepta" class="terms-text">
                            Autorizo la vinculación de este teléfono celular como mi dispositivo oficial de asistencia bajo firma criptográfica ES256.
                        </label>
                    </div>

                    <button type="submit" class="submit-btn" id="submitBtn">
                        {{textoBoton}}
                    </button>
                </form>
            </div>

            <script>
                const esWifi = {{esWifi.ToString().ToLowerInvariant()}};
                if (!esWifi) {
                    const gpsBox = document.getElementById('gps-status');
                    if (navigator.geolocation) {
                        const setPosOk = (pos) => {
                            document.getElementById('lat').value = pos.coords.latitude;
                            document.getElementById('lon').value = pos.coords.longitude;
                            document.getElementById('acc').value = pos.coords.accuracy;
                            gpsBox.style.background = 'rgba(16, 185, 129, 0.15)';
                            gpsBox.style.borderColor = 'rgba(16, 185, 129, 0.3)';
                            gpsBox.style.color = '#34d399';
                            gpsBox.innerHTML = '✅ <b>Ubicación fijada:</b> Precisión de ' + Math.round(pos.coords.accuracy) + 'm';
                        };

                        const setPosErr = (err) => {
                            gpsBox.style.background = 'rgba(239, 68, 68, 0.15)';
                            gpsBox.style.borderColor = 'rgba(239, 68, 68, 0.3)';
                            gpsBox.style.color = '#fca5a5';
                            if (err.code === 1) {
                                gpsBox.innerHTML = '⚠️ <b>Permiso Denegado:</b> En tu navegador toca el candado 🔒 y permite el acceso a <b>Ubicación</b>.';
                            } else if (err.code === 2) {
                                gpsBox.innerHTML = '⚠️ <b>GPS Desactivado:</b> Activa la Ubicación en los ajustes rápidos de tu celular.';
                            } else {
                                gpsBox.innerHTML = '📡 <b>Buscando satélites bajo techo...</b> Puedes presionar Vincular o acercarte a una puerta.';
                            }
                        };

                        navigator.geolocation.getCurrentPosition(
                            setPosOk,
                            (err1) => {
                                navigator.geolocation.getCurrentPosition(
                                    setPosOk,
                                    setPosErr,
                                    { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 }
                                );
                            },
                            { enableHighAccuracy: false, timeout: 6000, maximumAge: 300000 }
                        );
                    } else {
                        gpsBox.style.background = 'rgba(239, 68, 68, 0.15)';
                        gpsBox.style.borderColor = 'rgba(239, 68, 68, 0.3)';
                        gpsBox.style.color = '#fca5a5';
                        gpsBox.innerHTML = '⚠️ Tu navegador no soporta geolocalización GPS.';
                    }
                }
            </script>
        </body>
        </html>
        """;
    }

    public static string RenderGpsAutoVerifyView(
        Guid sesionId,
        string qrToken,
        string materiaNombre,
        string materiaCodigo,
        int? radioTolerancia = null,
        double? latReferencia = null,
        double? lonReferencia = null)
    {
        var materia = EncodeHtml(materiaNombre);
        var codigoMat = EncodeHtml(materiaCodigo);
        var tokenSeguro = EncodeHtml(qrToken);
        var latReferenciaString = latReferencia?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var lonReferenciaString = lonReferencia?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";

        return $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Verificando GPS en Aula | SLAC</title>
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" rel="stylesheet">
            <style>
                :root {
                    --bg-dark: #090d16;
                    --card-bg: rgba(17, 24, 39, 0.85);
                    --border: rgba(255, 255, 255, 0.08);
                    --primary: #4f46e5;
                    --success: #10b981;
                    --text-main: #f8fafc;
                    --text-muted: #94a3b8;
                }
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: 'Plus Jakarta Sans', sans-serif; }
                body {
                    background: radial-gradient(circle at top center, #1e1b4b 0%, var(--bg-dark) 70%);
                    color: var(--text-main);
                    min-height: 100vh;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    padding: 1.25rem;
                }
                .card {
                    background: var(--card-bg);
                    backdrop-filter: blur(16px);
                    border: 1px solid var(--border);
                    border-radius: 1.5rem;
                    padding: 2.25rem 1.75rem;
                    width: 100%;
                    max-width: 440px;
                    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.6);
                    text-align: center;
                    animation: fadeIn 0.3s ease-out;
                }
                @keyframes fadeIn { from { opacity: 0; transform: scale(0.98); } to { opacity: 1; transform: scale(1); } }
                .radar-box {
                    position: relative;
                    width: 90px;
                    height: 90px;
                    margin: 0 auto 1.5rem auto;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                }
                .pulse-ring {
                    position: absolute;
                    width: 100%;
                    height: 100%;
                    border-radius: 50%;
                    border: 2px solid #6366f1;
                    animation: pulse 2s cubic-bezier(0.215, 0.61, 0.355, 1) infinite;
                }
                .pulse-ring:nth-child(2) { animation-delay: 0.6s; }
                .pulse-ring:nth-child(3) { animation-delay: 1.2s; }
                @keyframes pulse {
                    0% { transform: scale(0.6); opacity: 0.9; }
                    100% { transform: scale(1.6); opacity: 0; }
                }
                .radar-core {
                    position: relative;
                    width: 60px;
                    height: 60px;
                    background: rgba(79, 70, 229, 0.2);
                    border: 2px solid #6366f1;
                    border-radius: 50%;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    color: #818cf8;
                    z-index: 2;
                }
                .tag {
                    display: inline-block;
                    background: rgba(99, 102, 241, 0.15);
                    color: #818cf8;
                    font-size: 0.75rem;
                    font-weight: 700;
                    padding: 0.3rem 0.75rem;
                    border-radius: 9999px;
                    letter-spacing: 0.05em;
                    text-transform: uppercase;
                    margin-bottom: 0.5rem;
                    border: 1px solid rgba(99, 102, 241, 0.3);
                }
                h1 { font-size: 1.4rem; font-weight: 800; margin-bottom: 0.4rem; }
                p.sub { font-size: 0.875rem; color: var(--text-muted); margin-bottom: 1.25rem; line-height: 1.4; }
                .status-badge {
                    background: rgba(255, 255, 255, 0.04);
                    border: 1px solid rgba(255, 255, 255, 0.08);
                    border-radius: 0.75rem;
                    padding: 0.75rem 1rem;
                    font-size: 0.875rem;
                    color: #cbd5e1;
                    margin-bottom: 1.5rem;
                }
                .btn {
                    width: 100%;
                    background: var(--primary);
                    color: #fff;
                    font-size: 0.9375rem;
                    font-weight: 700;
                    padding: 0.85rem;
                    border: none;
                    border-radius: 0.75rem;
                    cursor: pointer;
                    display: none;
                }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="radar-box">
                    <div class="pulse-ring"></div>
                    <div class="pulse-ring"></div>
                    <div class="radar-core">
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                            <polygon points="3 11 22 2 13 21 11 13 3 11"></polygon>
                        </svg>
                    </div>
                </div>

                <div class="tag">{{codigoMat}}</div>
                <h1>{{materia}}</h1>
                <p class="sub">Dispositivo oficial reconocido. Validando tu presencia física en el aula mediante GPS...</p>

                <div class="status-badge" id="statusBadge">
                    📡 Conectando con satélites GPS...
                </div>

                <form method="POST" action="/a/{{sesionId}}/confirmar-gps" id="gpsForm">
                    <input type="hidden" name="t" value="{{tokenSeguro}}" />
                    <input type="hidden" name="r" value="{{radioTolerancia}}" />
                    <input type="hidden" name="clat" value="{{latReferenciaString}}" />
                    <input type="hidden" name="clon" value="{{lonReferenciaString}}" />
                    <input type="hidden" id="lat" name="lat" />
                    <input type="hidden" id="lon" name="lon" />
                    <input type="hidden" id="acc" name="acc" />
                </form>

                <button class="btn" id="retryBtn" onclick="solicitarUbicacion()" style="margin-top: 0.5rem;">
                    🔄 Reintentar Detección GPS
                </button>
            </div>

            <script>
                function solicitarUbicacion() {
                    const badge = document.getElementById('statusBadge');
                    const retryBtn = document.getElementById('retryBtn');

                    badge.innerHTML = '📡 Conectando con satélites GPS en aula...';
                    badge.style.color = '#93c5fd';
                    badge.style.background = 'rgba(59, 130, 246, 0.1)';
                    badge.style.borderColor = 'rgba(99, 102, 241, 0.3)';
                    retryBtn.style.display = 'none';

                    if (!navigator.geolocation) {
                        badge.innerHTML = '⚠️ Tu navegador móvil no soporta geolocalización GPS.';
                        badge.style.color = '#fca5a5';
                        return;
                    }

                    const onPosOk = (pos) => {
                        badge.innerHTML = '✅ Ubicación verificada (' + Math.round(pos.coords.accuracy) + 'm). Registrando asistencia...';
                        badge.style.color = '#34d399';
                        badge.style.background = 'rgba(16, 185, 129, 0.15)';
                        document.getElementById('lat').value = pos.coords.latitude;
                        document.getElementById('lon').value = pos.coords.longitude;
                        document.getElementById('acc').value = pos.coords.accuracy;
                        document.getElementById('gpsForm').submit();
                    };

                    const onPosErr = (err) => {
                        badge.style.background = 'rgba(239, 68, 68, 0.15)';
                        badge.style.borderColor = 'rgba(239, 68, 68, 0.3)';
                        badge.style.color = '#fca5a5';
                        retryBtn.style.display = 'block';

                        if (err.code === 1) { // PERMISSION_DENIED
                            badge.innerHTML = '⚠️ <b>Permiso Denegado:</b> En tu navegador móvil toca el candado 🔒 de la barra de direcciones y activa <b>Ubicación</b>.';
                        } else if (err.code === 2) { // POSITION_UNAVAILABLE
                            badge.innerHTML = '⚠️ <b>GPS Desactivado:</b> Enciende la Ubicación en la barra de ajustes rápidos de tu celular.';
                        } else if (err.code === 3) { // TIMEOUT
                            badge.innerHTML = '📡 <b>Buscando señal bajo techo...</b> Reintentando con red celular/Wi-Fi...';
                            navigator.geolocation.getCurrentPosition(onPosOk, (errFinal) => {
                                badge.innerHTML = '⚠️ <b>Señal GPS débil bajo techo:</b> Pulsa "Reintentar" o acércate a una puerta/ventana.';
                            }, { enableHighAccuracy: false, timeout: 12000, maximumAge: 600000 });
                        } else {
                            badge.innerHTML = '⚠️ <b>Error de ubicación:</b> ' + (err.message || 'No se pudo obtener coordenadas.');
                        }
                    };

                    navigator.geolocation.getCurrentPosition(
                        onPosOk,
                        (errNormal) => {
                            navigator.geolocation.getCurrentPosition(
                                onPosOk,
                                onPosErr,
                                { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 }
                            );
                        },
                        { enableHighAccuracy: false, timeout: 6000, maximumAge: 300000 }
                    );
                }

                window.addEventListener('DOMContentLoaded', solicitarUbicacion);
            </script>
        </body>
        </html>
        """;
    }

    public static string RenderErrorView(string titulo, string detalle)
    {
        var tit = EncodeHtml(titulo);
        var det = EncodeHtml(detalle);

        return $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Error de Asistencia | SLAC</title>
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" rel="stylesheet">
            <style>
                :root {
                    --bg-dark: #090d16;
                    --card-bg: rgba(17, 24, 39, 0.9);
                    --border: rgba(255, 255, 255, 0.1);
                    --danger: #ef4444;
                    --text-main: #f8fafc;
                    --text-muted: #94a3b8;
                }
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: 'Plus Jakarta Sans', sans-serif; }
                body {
                    background: radial-gradient(circle at top center, #3b0712 0%, var(--bg-dark) 75%);
                    color: var(--text-main);
                    min-height: 100vh;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    padding: 1.25rem;
                }
                .card {
                    background: var(--card-bg);
                    backdrop-filter: blur(16px);
                    border: 1px solid var(--border);
                    border-radius: 1.5rem;
                    padding: 2.25rem 1.75rem;
                    width: 100%;
                    max-width: 420px;
                    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.6);
                    text-align: center;
                }
                .icon-circle {
                    width: 72px;
                    height: 72px;
                    background: rgba(239, 68, 68, 0.15);
                    border: 2px solid rgba(239, 68, 68, 0.4);
                    border-radius: 50%;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    margin: 0 auto 1.5rem auto;
                    color: var(--danger);
                }
                h1 { font-size: 1.375rem; font-weight: 800; margin-bottom: 0.75rem; line-height: 1.2; }
                p.desc { font-size: 0.9375rem; color: var(--text-muted); line-height: 1.5; margin-bottom: 1.5rem; }
                .notice {
                    font-size: 0.8125rem;
                    background: rgba(255, 255, 255, 0.03);
                    border: 1px solid rgba(255, 255, 255, 0.07);
                    padding: 0.875rem;
                    border-radius: 0.75rem;
                    color: #cbd5e1;
                }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="icon-circle">
                    <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                        <circle cx="12" cy="12" r="10"></circle>
                        <line x1="15" y1="9" x2="9" y2="15"></line>
                        <line x1="9" y1="9" x2="15" y2="15"></line>
                    </svg>
                </div>
                <h1>{{tit}}</h1>
                <p class="desc">{{det}}</p>
                <div class="notice">
                    Si estás en el aula, solicita al docente proyectar el código QR actualizado o comunícate con soporte de tu institución.
                </div>
                <button onclick="window.location.reload()" style="margin-top: 1.25rem; width: 100%; background: #4f46e5; color: #fff; font-weight: 700; padding: 0.75rem; border: none; border-radius: 0.75rem; cursor: pointer; font-size: 0.9rem;">
                    🔄 Reintentar Escaneo / Actualizar
                </button>
            </div>
        </body>
        </html>
        """;
    }

    public static string RenderRevinculacionPendingView(
        string? estudianteNombre,
        string? estudianteCodigo,
        string? materiaNombre,
        string? mensaje,
        Guid? solicitudId = null,
        Guid? sesionId = null)
    {
        var nom = EncodeHtml(estudianteNombre ?? "Estudiante");
        var cod = EncodeHtml(estudianteCodigo ?? "");
        var mat = EncodeHtml(materiaNombre ?? "Clase");
        var msg = EncodeHtml(mensaje ?? "Solicita a tu docente en el aula que autorice tu nuevo dispositivo.");
        var solIdStr = solicitudId?.ToString() ?? "";
        var sesIdStr = sesionId?.ToString() ?? "";

        return $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Revinculación en Espera | SLAC</title>
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" rel="stylesheet">
            <style>
                :root {
                    --bg-dark: #090d16;
                    --card-bg: rgba(17, 24, 39, 0.9);
                    --border: rgba(245, 158, 11, 0.25);
                    --warning: #f59e0b;
                    --text-main: #f8fafc;
                    --text-muted: #94a3b8;
                }
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: 'Plus Jakarta Sans', sans-serif; }
                body {
                    background: radial-gradient(circle at top center, #311c05 0%, var(--bg-dark) 70%);
                    color: var(--text-main);
                    min-height: 100vh;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    padding: 1.25rem;
                }
                .card {
                    background: var(--card-bg);
                    backdrop-filter: blur(16px);
                    border: 1px solid var(--border);
                    border-radius: 1.5rem;
                    padding: 2.25rem 1.75rem;
                    width: 100%;
                    max-width: 440px;
                    box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.6), 0 0 40px -10px rgba(245, 158, 11, 0.15);
                    text-align: center;
                    animation: slideUp 0.3s ease-out;
                }
                @keyframes slideUp { from { opacity: 0; transform: translateY(12px); } to { opacity: 1; transform: translateY(0); } }
                .icon-circle {
                    width: 72px;
                    height: 72px;
                    background: rgba(245, 158, 11, 0.15);
                    border: 2px solid rgba(245, 158, 11, 0.4);
                    border-radius: 50%;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    margin: 0 auto 1.25rem auto;
                    color: var(--warning);
                }
                .badge {
                    display: inline-block;
                    background: rgba(245, 158, 11, 0.15);
                    color: #fbbf24;
                    font-size: 0.75rem;
                    font-weight: 700;
                    padding: 0.3rem 0.8rem;
                    border-radius: 9999px;
                    letter-spacing: 0.05em;
                    text-transform: uppercase;
                    margin-bottom: 0.75rem;
                    border: 1px solid rgba(245, 158, 11, 0.3);
                }
                h1 { font-size: 1.375rem; font-weight: 800; margin-bottom: 0.5rem; }
                p.desc { font-size: 0.9375rem; color: var(--text-muted); line-height: 1.5; margin-bottom: 1.5rem; }
                .info-box {
                    background: rgba(255, 255, 255, 0.04);
                    border: 1px solid rgba(255, 255, 255, 0.08);
                    border-radius: 1rem;
                    padding: 1rem;
                    text-align: left;
                    margin-bottom: 1.5rem;
                    font-size: 0.875rem;
                }
                .btn {
                    display: block;
                    width: 100%;
                    background: linear-gradient(135deg, #d97706 0%, #b45309 100%);
                    color: #fff;
                    font-size: 0.9375rem;
                    font-weight: 700;
                    padding: 0.875rem;
                    border-radius: 0.875rem;
                    border: none;
                    cursor: pointer;
                    text-decoration: none;
                    transition: transform 0.15s, box-shadow 0.15s;
                }
                .btn:active { transform: scale(0.98); }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="icon-circle" id="iconBox">
                    <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                        <rect x="5" y="2" width="14" height="20" rx="2" ry="2"></rect>
                        <line x1="12" y1="18" x2="12.01" y2="18"></line>
                    </svg>
                </div>
                <div class="badge" id="statusBadge">Revinculación en Proceso</div>
                <h1 id="statusTitle">Solicitud de Autorización</h1>
                <p class="desc" id="statusDesc">{{msg}}</p>

                <div class="info-box">
                    <div style="margin-bottom: 0.4rem;"><b>Estudiante:</b> {{nom}}</div>
                    <div style="margin-bottom: 0.4rem;"><b>Matrícula:</b> {{cod}}</div>
                    <div><b>Asignatura:</b> {{mat}}</div>
                </div>

                <button onclick="window.location.reload();" class="btn" id="reloadBtn">
                    Recargar Página (Verificar Aprobación)
                </button>
            </div>

            <script>
                const solId = "{{solIdStr}}";
                const sesId = "{{sesIdStr}}";
                if (solId && solId.length > 10) {
                    const timer = setInterval(async () => {
                        try {
                            const res = await fetch(`/a/revinculacion-status/${solId}?sesionId=${sesId}`);
                            if (res.ok) {
                                const data = await res.json();
                                if (data && data.estado === "aprobada") {
                                    clearInterval(timer);
                                    const b = document.getElementById("statusBadge");
                                    if (b) {
                                        b.style.background = "rgba(16, 185, 129, 0.2)";
                                        b.style.color = "#10b981";
                                        b.style.borderColor = "rgba(16, 185, 129, 0.5)";
                                        b.innerText = "¡AUTORIZADO POR EL DOCENTE!";
                                    }
                                    const t = document.getElementById("statusTitle");
                                    if (t) t.innerText = "¡Dispositivo Aprobado!";
                                    const d = document.getElementById("statusDesc");
                                    if (d) d.innerText = "Tu docente ha autorizado este teléfono exitosamente. Registrando tu asistencia en pantalla...";
                                    setTimeout(() => {
                                        window.location.reload();
                                    }, 1000);
                                }
                            }
                        } catch (e) {
                            console.warn("Poll check error:", e);
                        }
                    }, 2000);
                }
            </script>
        </body>
        </html>
        """;
    }

    private static string EncodeHtml(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }
}
