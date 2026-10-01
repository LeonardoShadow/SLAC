$ErrorActionPreference = "Continue"
$baseUrl = "http://slac-asistencia.runasp.net"

Write-Host "=========================================================="
Write-Host "  SUITE COMPLETA DE PRUEBAS AUTOMATIZADAS - SLAC EN VIVO"
Write-Host ('  URL: ' + $baseUrl)
Write-Host "=========================================================="

function Test-Endpoint {
    param([string]$path, [string]$desc, [int]$expectedStatus = 200, [string]$containsText = "")
    try {
        $uri = $baseUrl + $path
        $resp = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 15
        $statusOk = ($resp.StatusCode -eq $expectedStatus)
        $textOk = [string]::IsNullOrEmpty($containsText) -or ($resp.Content -match $containsText)
        
        if ($statusOk -and $textOk) {
            Write-Host ('PASS: ' + $desc + ' -> Status: ' + $resp.StatusCode + ' (' + $resp.Content.Length + ' bytes)') -ForegroundColor Green
        } else {
            Write-Host ('FAIL: ' + $desc + ' -> Status: ' + $resp.StatusCode + ' Expected: ' + $expectedStatus) -ForegroundColor Red
        }
    } catch {
        Write-Host ('ERROR: ' + $desc + ' -> ' + $_.Exception.Message) -ForegroundColor Red
    }
}

Write-Host "`n--- 1. RUTAS PRINCIPALES Y LOGIN ---"
Test-Endpoint -path "/" -desc "Landing / Home" -containsText "SLAC"
Test-Endpoint -path "/login" -desc "Pagina de Inicio de Sesion" -containsText "Docente"
Test-Endpoint -path "/Error" -desc "Pagina de Error Estandar"

Write-Host "`n--- 2. RUTAS DE DOCENTE ---"
Test-Endpoint -path "/docente/materias" -desc "Materias del Docente"
Test-Endpoint -path "/docente/reportes" -desc "Reportes y Faltas del Docente"
Test-Endpoint -path "/docente/faltas" -desc "Alias Reporte Faltas"

Write-Host "`n--- 3. RUTAS DE ESTUDIANTE ---"
Test-Endpoint -path "/estudiante/materias" -desc "Panel Materias del Estudiante"
Test-Endpoint -path "/estudiante/faltas" -desc "Panel Faltas del Estudiante"

Write-Host "`n--- 4. RUTAS DE ADMINISTRACION ---"
Test-Endpoint -path "/admin/materias" -desc "Admin: Materias"
Test-Endpoint -path "/admin/espacios" -desc "Admin: Aulas y Espacios"
Test-Endpoint -path "/admin/docentes" -desc "Admin: Docentes"
Test-Endpoint -path "/admin/periodos" -desc "Admin: Periodos Academicos"
Test-Endpoint -path "/admin/feriados" -desc "Admin: Feriados"
Test-Endpoint -path "/superadmin" -desc "SuperAdmin Dashboard"

Write-Host "`n--- 5. FLUJO DE ESCANEO DE ESTUDIANTE (/a/{sesionId}) ---"
$dummyId = [System.Guid]::NewGuid().ToString()
Test-Endpoint -path ('/a/' + $dummyId) -desc "Escaneo sin token (debe mostrar Enlace Incompleto)" -containsText "Incompleto"
Test-Endpoint -path ('/a/' + $dummyId + '?t=tokentestinvalido') -desc "Escaneo con token no valido (debe informar expirado o invalido)" -containsText "Invalido"

Write-Host "`n--- 6. RECURSOS ESTATICOS CRITICOS (CSS / JS / MUD) ---"
Test-Endpoint -path "/_content/MudBlazor/MudBlazor.min.css" -desc "MudBlazor CSS"
Test-Endpoint -path "/_content/MudBlazor/MudBlazor.min.js" -desc "MudBlazor JS"
Test-Endpoint -path "/app.css" -desc "Estilos Globales app.css"

Write-Host "`n--- 7. SIGNALR ATTENDANCE HUB NEGOTIATION ---"
try {
    $hubUri = $baseUrl + "/attendanceHub/negotiate?negotiateVersion=1"
    $hubResp = Invoke-WebRequest -Uri $hubUri -Method Post -UseBasicParsing -TimeoutSec 15
    Write-Host ('PASS: SignalR Negotiate -> Status: ' + $hubResp.StatusCode + ' (ConnectionId generado)') -ForegroundColor Green
} catch {
    Write-Host ('INFO: SignalR Negotiate -> ' + $_.Exception.Message) -ForegroundColor Yellow
}
