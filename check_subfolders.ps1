$url = "http://slac-asistencia.runasp.net"
$subpaths = @(
    "slac_publish",
    "slac_publish/login",
    "Publish",
    "Publish/login",
    "bin",
    "bin/Publish",
    "bin/Publish/login"
)

foreach ($p in $subpaths) {
    try {
        $res = Invoke-WebRequest -Uri "$url/$p" -UseBasicParsing -TimeoutSec 5
        Write-Host "$p -> Status: $($res.StatusCode)"
    } catch {
        Write-Host "$p -> $_"
    }
}
