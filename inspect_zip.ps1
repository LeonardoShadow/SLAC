Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead('d:\Universidad\AIGENTIC_N8N\SLAC\SLAC\slac_publish.zip')
$cfg = $zip.GetEntry('web.config')
Write-Host "web.config exists in root of zip: $($cfg -ne $null)"
$dll = $zip.GetEntry('SLAC.dll')
Write-Host "SLAC.dll exists in root of zip: $($dll -ne $null)"
$zip.Dispose()
