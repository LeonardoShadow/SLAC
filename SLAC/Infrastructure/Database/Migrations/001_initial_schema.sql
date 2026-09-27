-- =============================================================================
-- SLAC - Migración 001: Esquema Inicial de Configuración e Identidad
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- 1. Tabla de Instituciones (Tenant Raíz)
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

-- 2. Vínculo entre Usuarios de Auth y sus Instituciones con Rol
CREATE TABLE IF NOT EXISTS public.usuario_institucion (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    rol VARCHAR(50) NOT NULL CHECK (rol IN ('admin_plataforma', 'admin_institucional', 'docente')),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_usuario_institucion_rol UNIQUE (user_id, institucion_id, rol)
);

-- 3. Perfil de Docente
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

-- 4. Perfil del Estudiante (Sin credenciales en auth.users; unicidad institucional)
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
