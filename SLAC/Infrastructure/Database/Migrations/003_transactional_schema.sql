-- =============================================================================
-- SLAC - Migración 003: Entidades Transaccionales y de Dispositivos
-- =============================================================================

-- 1. Registro de Dispositivos (Sin datos personales)
CREATE TABLE IF NOT EXISTS public.dispositivo (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    jti_hash VARCHAR(64) NOT NULL UNIQUE, -- SHA256 del identificador único del token
    kid VARCHAR(50) NOT NULL, -- Key Identifier para rotación de claves ES256
    emitido_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    ultimo_uso_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    revocado_en TIMESTAMPTZ,
    agente_resumen VARCHAR(255)
);

-- 2. Vinculación Dispositivo - Estudiante (Regla RN-05: 1 dispositivo activo por estudiante por institución)
CREATE TABLE IF NOT EXISTS public.dispositivo_estudiante (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    dispositivo_id UUID NOT NULL REFERENCES public.dispositivo(id) ON DELETE CASCADE,
    estudiante_id UUID NOT NULL REFERENCES public.estudiante(id) ON DELETE CASCADE,
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    activo BOOLEAN NOT NULL DEFAULT true,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Asegurar que sólo haya 1 dispositivo activo por estudiante por institución
CREATE UNIQUE INDEX IF NOT EXISTS uq_idx_estudiante_dispositivo_activo 
ON public.dispositivo_estudiante (institucion_id, estudiante_id) 
WHERE activo = true;

-- Asegurar que 1 dispositivo pertenezca a 1 solo estudiante activo en una institución
CREATE UNIQUE INDEX IF NOT EXISTS uq_idx_dispositivo_institucion_activo 
ON public.dispositivo_estudiante (institucion_id, dispositivo_id) 
WHERE activo = true;

-- 3. Tabla Maestra: Sesión / Lista de Asistencia diaria
CREATE TABLE IF NOT EXISTS public.lista_asistencia (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    materia_id UUID NOT NULL REFERENCES public.materia(id) ON DELETE CASCADE,
    docente_id UUID NOT NULL REFERENCES public.docente(id) ON DELETE CASCADE,
    espacio_id UUID NOT NULL REFERENCES public.espacio(id) ON DELETE RESTRICT,
    fecha DATE NOT NULL,
    hora_inicio TIME NOT NULL,
    hora_cierre TIME,
    estado VARCHAR(20) NOT NULL DEFAULT 'Programada' CHECK (estado IN ('Programada', 'Abierta', 'Cerrada', 'Suspendida')),
    url_detalle VARCHAR(500),
    total_suscritos INT NOT NULL DEFAULT 0,
    total_presentes INT NOT NULL DEFAULT 0,
    total_faltas INT NOT NULL DEFAULT 0,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    actualizado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_materia_fecha UNIQUE (materia_id, fecha)
);

-- 4. Detalle de Asistencia por Estudiante
CREATE TABLE IF NOT EXISTS public.asistencia_detalle (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    lista_id UUID NOT NULL REFERENCES public.lista_asistencia(id) ON DELETE CASCADE,
    estudiante_id UUID NOT NULL REFERENCES public.estudiante(id) ON DELETE CASCADE,
    estado VARCHAR(20) NOT NULL CHECK (estado IN ('Presente', 'Falta')),
    origen VARCHAR(30) NOT NULL CHECK (origen IN ('QR', 'Cierre', 'Suscripción tardía', 'Corrección')),
    hora_llegada TIMESTAMPTZ,
    minutos_desde_inicio INT,
    dispositivo_id UUID REFERENCES public.dispositivo(id) ON DELETE SET NULL,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_lista_estudiante UNIQUE (lista_id, estudiante_id)
);

-- 5. Revinculación de Dispositivo Autorizada por Docente
CREATE TABLE IF NOT EXISTS public.revinculacion (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    estudiante_id UUID NOT NULL REFERENCES public.estudiante(id) ON DELETE CASCADE,
    materia_id UUID NOT NULL REFERENCES public.materia(id) ON DELETE CASCADE,
    docente_id UUID NOT NULL REFERENCES public.docente(id) ON DELETE CASCADE,
    expira_en TIMESTAMPTZ NOT NULL,
    usada_en TIMESTAMPTZ,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- 6. Auditoría Inmutable de Eventos Sensibles
CREATE TABLE IF NOT EXISTS public.auditoria (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID REFERENCES public.institucion(id) ON DELETE SET NULL,
    actor VARCHAR(150) NOT NULL,
    evento VARCHAR(100) NOT NULL,
    entidad VARCHAR(100) NOT NULL,
    datos JSONB,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now()
);
