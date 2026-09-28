-- =============================================================================
-- SLAC - Migración 002: Entidades Académicas
-- =============================================================================

-- 1. Catálogo de Espacios (Aulas y Laboratorios)
CREATE TABLE IF NOT EXISTS public.espacio (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    nombre VARCHAR(100) NOT NULL,
    tipo VARCHAR(50) NOT NULL CHECK (tipo IN ('aula', 'laboratorio')),
    capacidad INT NOT NULL DEFAULT 30 CHECK (capacidad > 0),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_espacio_nombre UNIQUE (institucion_id, nombre)
);

-- 2. Periodos Académicos
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

-- 3. Días No Lectivos (Feriados y Suspensiones)
CREATE TABLE IF NOT EXISTS public.dia_no_lectivo (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    fecha DATE NOT NULL,
    motivo VARCHAR(255) NOT NULL,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_dia_no_lectivo UNIQUE (institucion_id, fecha)
);

-- 4. Materias Publicadas
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
    dias VARCHAR(50) NOT NULL, -- Ej: 'L,M,X,J,V'
    estado VARCHAR(20) NOT NULL DEFAULT 'activa' CHECK (estado IN ('activa', 'archivada')),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_materia_grupo UNIQUE (institucion_id, periodo_id, codigo, grupo)
);

-- 5. Suscripción de Estudiantes a Materias (Primer Escaneo)
CREATE TABLE IF NOT EXISTS public.suscripcion (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    materia_id UUID NOT NULL REFERENCES public.materia(id) ON DELETE CASCADE,
    estudiante_id UUID NOT NULL REFERENCES public.estudiante(id) ON DELETE CASCADE,
    fecha TIMESTAMPTZ NOT NULL DEFAULT now(),
    lista_origen_id UUID, -- Se asociará con lista_asistencia
    estado VARCHAR(20) NOT NULL DEFAULT 'activa' CHECK (estado IN ('activa', 'retirada')),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_suscripcion UNIQUE (materia_id, estudiante_id)
);
