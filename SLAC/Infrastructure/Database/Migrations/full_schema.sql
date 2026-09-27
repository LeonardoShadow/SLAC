-- =============================================================================
-- SLAC (Sistema de Lista de Asistencia a Clases)
-- Script Completo de Base de Datos para Supabase (PostgreSQL 15+)
-- Incluye: Configuración, Académico, Transaccional, RLS, Vistas e Índices
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- -----------------------------------------------------------------------------
-- 1. TABLAS DE CONFIGURACIÓN E IDENTIDAD
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.institucion (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    nombre VARCHAR(255) NOT NULL,
    tipo VARCHAR(50) NOT NULL CHECK (tipo IN ('universidad', 'colegio', 'instituto')),
    zona_horaria VARCHAR(100) NOT NULL DEFAULT 'America/La_Paz',
    ventana_min INT NOT NULL DEFAULT 20 CHECK (ventana_min > 0),
    rotacion_seg INT NOT NULL DEFAULT 15 CHECK (rotacion_seg > 0),
    estado VARCHAR(20) NOT NULL DEFAULT 'activo' CHECK (estado IN ('activo', 'inactivo', 'suspendido')),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    actualizado_en TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS public.usuario_institucion (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    rol VARCHAR(50) NOT NULL CHECK (rol IN ('admin_plataforma', 'admin_institucional', 'docente')),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_usuario_institucion_rol UNIQUE (user_id, institucion_id, rol)
);

CREATE TABLE IF NOT EXISTS public.docente (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    nombres VARCHAR(150) NOT NULL,
    apellidos VARCHAR(150) NOT NULL,
    correo VARCHAR(255) NOT NULL,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_docente_user UNIQUE (institucion_id, user_id),
    CONSTRAINT uq_docente_correo UNIQUE (institucion_id, correo)
);

CREATE TABLE IF NOT EXISTS public.estudiante (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    codigo VARCHAR(50) NOT NULL,
    nombres VARCHAR(150) NOT NULL,
    apellidos VARCHAR(150) NOT NULL,
    correo VARCHAR(255) NOT NULL,
    consentimiento_en TIMESTAMPTZ,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_estudiante_codigo UNIQUE (institucion_id, codigo)
);

-- -----------------------------------------------------------------------------
-- 2. TABLAS ACADÉMICAS
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.espacio (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    nombre VARCHAR(100) NOT NULL,
    tipo VARCHAR(50) NOT NULL CHECK (tipo IN ('aula', 'laboratorio')),
    capacidad INT NOT NULL DEFAULT 30 CHECK (capacidad > 0),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_espacio_nombre UNIQUE (institucion_id, nombre)
);

CREATE TABLE IF NOT EXISTS public.periodo_academico (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    nombre VARCHAR(100) NOT NULL,
    fecha_inicio DATE NOT NULL,
    fecha_fin DATE NOT NULL,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_fechas_periodo CHECK (fecha_fin >= fecha_inicio),
    CONSTRAINT uq_periodo_nombre UNIQUE (institucion_id, nombre)
);

CREATE TABLE IF NOT EXISTS public.dia_no_lectivo (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    fecha DATE NOT NULL,
    motivo VARCHAR(255) NOT NULL,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_dia_no_lectivo UNIQUE (institucion_id, fecha)
);

CREATE TABLE IF NOT EXISTS public.materia (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    docente_id UUID NOT NULL REFERENCES public.docente(id) ON DELETE CASCADE,
    periodo_id UUID NOT NULL REFERENCES public.periodo_academico(id) ON DELETE RESTRICT,
    codigo VARCHAR(50) NOT NULL,
    nombre VARCHAR(200) NOT NULL,
    grupo VARCHAR(50) NOT NULL,
    espacio_id UUID NOT NULL REFERENCES public.espacio(id) ON DELETE RESTRICT,
    hora_inicio TIME NOT NULL,
    dias VARCHAR(50) NOT NULL,
    estado VARCHAR(20) NOT NULL DEFAULT 'activa' CHECK (estado IN ('activa', 'archivada')),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_materia_grupo UNIQUE (institucion_id, periodo_id, codigo, grupo)
);

CREATE TABLE IF NOT EXISTS public.suscripcion (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    materia_id UUID NOT NULL REFERENCES public.materia(id) ON DELETE CASCADE,
    estudiante_id UUID NOT NULL REFERENCES public.estudiante(id) ON DELETE CASCADE,
    fecha TIMESTAMPTZ NOT NULL DEFAULT now(),
    lista_origen_id UUID,
    estado VARCHAR(20) NOT NULL DEFAULT 'activa' CHECK (estado IN ('activa', 'retirada')),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_suscripcion UNIQUE (materia_id, estudiante_id)
);

-- -----------------------------------------------------------------------------
-- 3. TABLAS TRANSACCIONALES Y DISPOSITIVOS
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.dispositivo (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    jti_hash VARCHAR(64) NOT NULL UNIQUE,
    kid VARCHAR(50) NOT NULL,
    emitido_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    ultimo_uso_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    revocado_en TIMESTAMPTZ,
    agente_resumen VARCHAR(255)
);

CREATE TABLE IF NOT EXISTS public.dispositivo_estudiante (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    dispositivo_id UUID NOT NULL REFERENCES public.dispositivo(id) ON DELETE CASCADE,
    estudiante_id UUID NOT NULL REFERENCES public.estudiante(id) ON DELETE CASCADE,
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    activo BOOLEAN NOT NULL DEFAULT true,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_idx_estudiante_dispositivo_activo 
ON public.dispositivo_estudiante (institucion_id, estudiante_id) 
WHERE activo = true;

CREATE UNIQUE INDEX IF NOT EXISTS uq_idx_dispositivo_institucion_activo 
ON public.dispositivo_estudiante (institucion_id, dispositivo_id) 
WHERE activo = true;

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

CREATE TABLE IF NOT EXISTS public.auditoria (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID REFERENCES public.institucion(id) ON DELETE SET NULL,
    actor VARCHAR(150) NOT NULL,
    evento VARCHAR(100) NOT NULL,
    entidad VARCHAR(100) NOT NULL,
    datos JSONB,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- 4. POLÍTICAS RLS (ROW LEVEL SECURITY)
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION public.slac_current_user_instituciones()
RETURNS SETOF UUID
LANGUAGE sql
SECURITY DEFINER
STABLE
AS $$
    SELECT institucion_id 
    FROM public.usuario_institucion 
    WHERE user_id = auth.uid();
$$;

CREATE OR REPLACE FUNCTION public.slac_user_has_role(inst_id UUID, req_rol VARCHAR)
RETURNS BOOLEAN
LANGUAGE sql
SECURITY DEFINER
STABLE
AS $$
    SELECT EXISTS (
        SELECT 1 
        FROM public.usuario_institucion 
        WHERE user_id = auth.uid() 
          AND institucion_id = inst_id 
          AND (rol = req_rol OR rol = 'admin_plataforma')
    );
$$;

ALTER TABLE public.institucion ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.usuario_institucion ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.docente ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.estudiante ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.espacio ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.periodo_academico ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.dia_no_lectivo ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.materia ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.suscripcion ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.dispositivo ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.dispositivo_estudiante ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.lista_asistencia ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.asistencia_detalle ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.revinculacion ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.auditoria ENABLE ROW LEVEL SECURITY;

CREATE POLICY rls_institucion_select ON public.institucion
    FOR SELECT TO authenticated
    USING (id IN (SELECT public.slac_current_user_instituciones()));

CREATE POLICY rls_usuario_institucion_select ON public.usuario_institucion
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

CREATE POLICY rls_docente_select ON public.docente
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

CREATE POLICY rls_docente_admin ON public.docente
    FOR ALL TO authenticated
    USING (public.slac_user_has_role(institucion_id, 'admin_institucional'))
    WITH CHECK (public.slac_user_has_role(institucion_id, 'admin_institucional'));

CREATE POLICY rls_estudiante_select ON public.estudiante
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

CREATE POLICY rls_espacio_select ON public.espacio
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

CREATE POLICY rls_espacio_admin ON public.espacio
    FOR ALL TO authenticated
    USING (public.slac_user_has_role(institucion_id, 'admin_institucional'))
    WITH CHECK (public.slac_user_has_role(institucion_id, 'admin_institucional'));

CREATE POLICY rls_periodo_select ON public.periodo_academico
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

CREATE POLICY rls_periodo_admin ON public.periodo_academico
    FOR ALL TO authenticated
    USING (public.slac_user_has_role(institucion_id, 'admin_institucional'))
    WITH CHECK (public.slac_user_has_role(institucion_id, 'admin_institucional'));

CREATE POLICY rls_dia_no_lectivo_select ON public.dia_no_lectivo
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

CREATE POLICY rls_dia_no_lectivo_admin ON public.dia_no_lectivo
    FOR ALL TO authenticated
    USING (public.slac_user_has_role(institucion_id, 'admin_institucional'))
    WITH CHECK (public.slac_user_has_role(institucion_id, 'admin_institucional'));

CREATE POLICY rls_materia_select ON public.materia
    FOR SELECT TO authenticated
    USING (
        institucion_id IN (SELECT public.slac_current_user_instituciones())
        AND (
            docente_id IN (SELECT id FROM public.docente WHERE user_id = auth.uid())
            OR public.slac_user_has_role(institucion_id, 'admin_institucional')
        )
    );

CREATE POLICY rls_materia_modify ON public.materia
    FOR ALL TO authenticated
    USING (
        institucion_id IN (SELECT public.slac_current_user_instituciones())
        AND (
            docente_id IN (SELECT id FROM public.docente WHERE user_id = auth.uid())
            OR public.slac_user_has_role(institucion_id, 'admin_institucional')
        )
    )
    WITH CHECK (
        institucion_id IN (SELECT public.slac_current_user_instituciones())
        AND (
            docente_id IN (SELECT id FROM public.docente WHERE user_id = auth.uid())
            OR public.slac_user_has_role(institucion_id, 'admin_institucional')
        )
    );

CREATE POLICY rls_lista_asistencia_select ON public.lista_asistencia
    FOR SELECT TO authenticated
    USING (
        institucion_id IN (SELECT public.slac_current_user_instituciones())
        AND (
            docente_id IN (SELECT id FROM public.docente WHERE user_id = auth.uid())
            OR public.slac_user_has_role(institucion_id, 'admin_institucional')
        )
    );

CREATE POLICY rls_asistencia_detalle_select ON public.asistencia_detalle
    FOR SELECT TO authenticated
    USING (
        institucion_id IN (SELECT public.slac_current_user_instituciones())
    );

CREATE POLICY rls_auditoria_select ON public.auditoria
    FOR SELECT TO authenticated
    USING (
        public.slac_user_has_role(institucion_id, 'admin_institucional')
    );

-- -----------------------------------------------------------------------------
-- 5. VISTAS E ÍNDICES
-- -----------------------------------------------------------------------------
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

CREATE INDEX IF NOT EXISTS idx_asistencia_detalle_inst_lista 
ON public.asistencia_detalle (institucion_id, lista_id);

CREATE INDEX IF NOT EXISTS idx_asistencia_detalle_estudiante 
ON public.asistencia_detalle (estudiante_id);

CREATE INDEX IF NOT EXISTS idx_lista_asistencia_fecha_estado 
ON public.lista_asistencia (fecha, estado);

CREATE INDEX IF NOT EXISTS idx_suscripcion_materia 
ON public.suscripcion (materia_id, estudiante_id);

CREATE INDEX IF NOT EXISTS idx_dispositivo_jti 
ON public.dispositivo (jti_hash);
