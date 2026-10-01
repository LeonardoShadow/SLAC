$ErrorActionPreference = "Continue"
$baseUrl = "http://slac-asistencia.runasp.net"

Write-Host "--- TEST SIGNALR NEGOTIATE (/hubs/attendance) ---"
try {
    $hubUri = $baseUrl + "/hubs/attendance/negotiate?negotiateVersion=1"
    $hubResp = Invoke-WebRequest -Uri $hubUri -Method Post -UseBasicParsing -TimeoutSec 15
    Write-Host ("HUB STATUS: " + $hubResp.StatusCode)
    Write-Host ("HUB CONTENT: " + $hubResp.Content)
} catch {
    Write-Host ("HUB ERROR: " + $_.Exception.Message)
}

Write-Host "`n--- TEST ESCANEO CON TOKEN INVALIDO (/a/{guid}?t=invalido) ---"
try {
    $dummyId = [System.Guid]::NewGuid().ToString()
    $scanResp = Invoke-WebRequest -Uri ($baseUrl + "/a/" + $dummyId + "?t=tokentestinvalido") -UseBasicParsing -TimeoutSec 15
    Write-Host ("SCAN STATUS: " + $scanResp.StatusCode)
    Write-Host ("SCAN LENGTH: " + $scanResp.Content.Length)
    # Busquemos el título en el HTML
    if ($scanResp.Content -match "<title>(.*?)</title>") {
        Write-Host ("SCAN TITLE: " + $matches[1])
    }
    if ($scanResp.Content -match "<h[1-3].*?>(.*?)</h[1-3]>") {
        Write-Host ("SCAN HEADING: " + $matches[1])
    }
} catch {
    Write-Host ("SCAN ERROR: " + $_.Exception.Message)
}
