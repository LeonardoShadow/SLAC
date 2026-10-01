try {
    $resp = Invoke-WebRequest -Uri "https://slac-asistencia.runasp.net/login" -UseBasicParsing -TimeoutSec 10
    Write-Host "HTTPS STATUS: $($resp.StatusCode)"
} catch {
    Write-Host "HTTPS FAILED: $_"
}
