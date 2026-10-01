$ErrorActionPreference = "Stop"
Write-Host "1. Publicando proyecto en bin/Publish..."
dotnet publish -c Release -o bin/Publish

Write-Host "2. Empaquetando slac_publish.zip..."
$zipPath = "d:\Universidad\AIGENTIC_N8N\SLAC\SLAC\slac_publish.zip"
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}
Compress-Archive -Path "bin\Publish\*" -DestinationPath $zipPath -Force

$zip = Get-Item $zipPath
$mb = [math]::Round($zip.Length / 1MB, 2)
Write-Host "ZIP ACTUALIZADO EXITOSAMENTE: $($zip.FullName) ($mb MB)"
