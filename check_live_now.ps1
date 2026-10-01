$url = "http://slac-asistencia.runasp.net"
try {
    $res = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
    Write-Host "HOME Status: $($res.StatusCode)"
} catch {
    Write-Host "HOME Error: $_"
}

try {
    $res = Invoke-WebRequest -Uri "$url/login" -UseBasicParsing -TimeoutSec 10
    Write-Host "LOGIN Status: $($res.StatusCode)"
} catch {
    Write-Host "LOGIN Error: $_"
}
