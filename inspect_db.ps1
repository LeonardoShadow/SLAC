$config = Get-Content -Raw -Path "d:\Universidad\AIGENTIC_N8N\SLAC\SLAC\SLAC\appsettings.json" | ConvertFrom-Json
$url = $config.Supabase.Url
$key = $config.Supabase.Key

$headers = @{
    "apikey" = $key
    "Authorization" = "Bearer $key"
}

Write-Host "=== CONSULTANDO DATOS EXACTOS EN SUPABASE ==="

# 1. Institucion
$inst = Invoke-RestMethod -Uri "$url/rest/v1/institucion?select=id,nombre,codigo" -Headers $headers
Write-Host "`n[INSTITUCIONES]: $($inst.Count)"
foreach ($i in $inst) {
    Write-Host "  * $($i.nombre) (Codigo: $($i.codigo), ID: $($i.id))"
}

# 2. Docentes
$docentes = Invoke-RestMethod -Uri "$url/rest/v1/docente?select=id,nombres,apellidos,correo,codigo,institucion_id" -Headers $headers
Write-Host "`n[DOCENTES REGISTRADOS]: $($docentes.Count)"
foreach ($d in $docentes) {
    Write-Host "  * $($d.nombres) $($d.apellidos) | Codigo: $($d.codigo) | Correo: $($d.correo) | ID: $($d.id)"
}

# 3. Materias
$materias = Invoke-RestMethod -Uri "$url/rest/v1/materia?select=id,nombre,codigo,grupo,docente_id,dias,hora_inicio,estado" -Headers $headers
Write-Host "`n[MATERIAS REGISTRADAS]: $($materias.Count)"
foreach ($m in $materias) {
    Write-Host "  * $($m.nombre) ($($m.codigo) - Grupo $($m.grupo)) | Dias: $($m.dias) | Hora: $($m.hora_inicio) | Estado: $($m.estado) | ID: $($m.id)"
}

# 4. Suscripciones (Estudiantes inscritos)
$suscripciones = Invoke-RestMethod -Uri "$url/rest/v1/suscripcion?select=id,materia_id,estudiante_id,estado" -Headers $headers
Write-Host "`n[ESTUDIANTES INSCRITOS EN MATERIAS]: $($suscripciones.Count)"

# 5. Listas de asistencia (Sesiones)
$listas = Invoke-RestMethod -Uri "$url/rest/v1/lista_asistencia?select=id,materia_id,fecha,hora_inicio,hora_cierre,estado,total_suscritos,total_presentes,total_faltas&order=fecha.desc&limit=15" -Headers $headers
Write-Host "`n[ULTIMAS SESIONES / LISTAS DE ASISTENCIA]: $($listas.Count)"
foreach ($l in $listas) {
    Write-Host "  * Fecha: $($l.fecha) | Estado: $($l.estado) | Materia: $($l.materia_id) | Asistieron: $($l.total_presentes)/$($l.total_suscritos) | Faltas: $($l.total_faltas)"
}
