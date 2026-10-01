$url = "http://slac-asistencia.runasp.net"
$zips = @(
    "slac_publish.zip",
    "publish.zip",
    "slac.zip",
    "appsettings.json",
    "web.config"
)

foreach ($z in $zips) {
    try {
        $res = Invoke-WebRequest -Uri "$url/$z" -UseBasicParsing -TimeoutSec 5
        Write-Host "$z -> Status: $($res.StatusCode)"
    } catch {
        Write-Host "$z -> $($_.Exception.Message)"
    }
}
