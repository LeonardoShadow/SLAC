-- =============================================================================
-- SLAC - Migración 005: Vistas de Reportes e Índices de Alto Rendimiento
-- =============================================================================

-- 1. Vista de Reporte de Faltas (Optimizada para exportación a Excel / CSV)
-- Nota: En Postgres 15+ 'security_invoker = true' hace que la vista evalúe el RLS del usuario que consulta.
CREATE OR REPLACE VIEW public.v_faltas
WITH (security_invoker = true)
AS
SELECT 
    ad.institucion_id,
    ad.lista_id,
    e.codigo AS codigo_estudiante,
    e.apellidos,
    e.nombres,
    m.nombre AS materia,
    m.grupo,
    la.fecha,
    d.nombres || ' ' || d.apellidos AS docente,
    ad.origen,
    ad.creado_en AS registrado_en
FROM public.asistencia_detalle ad
JOIN public.lista_asistencia la ON la.id = ad.lista_id
JOIN public.materia m ON m.id = la.materia_id
JOIN public.docente d ON d.id = la.docente_id
JOIN public.estudiante e ON e.id = ad.estudiante_id
WHERE ad.estado = 'Falta';

-- 2. Índices Críticos para Alta Concurrencia y Filtros RLS (SRS 3.4 y DATABASE.md Sec. 5)
-- Índice compuesto sobre (institucion_id, lista_id) en asistencia_detalle
CREATE INDEX IF NOT EXISTS idx_asistencia_detalle_inst_lista 
ON public.asistencia_detalle (institucion_id, lista_id);

-- Índice por estudiante para consultas de suscripciones y faltas retroactivas
CREATE INDEX IF NOT EXISTS idx_asistencia_detalle_estudiante 
ON public.asistencia_detalle (estudiante_id);

-- Índice en lista_asistencia por fecha y estado (usado por el planificador / worker a las 00:05 y horas de clase)
CREATE INDEX IF NOT EXISTS idx_lista_asistencia_fecha_estado 
ON public.lista_asistencia (fecha, estado);

-- Índice para suscripción rápida por materia
CREATE INDEX IF NOT EXISTS idx_suscripcion_materia 
ON public.suscripcion (materia_id, estudiante_id);

-- Índice para validación de dispositivos por hash JTI
CREATE INDEX IF NOT EXISTS idx_dispositivo_jti 
ON public.dispositivo (jti_hash);
