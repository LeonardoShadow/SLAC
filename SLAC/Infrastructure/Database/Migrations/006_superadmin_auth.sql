-- 006_superadmin_auth.sql
-- Migración para añadir soporte de autenticación segura con contraseñas hasheadas (BCrypt)
-- para administradores institucionales y SuperAdmin.

CREATE TABLE IF NOT EXISTS public.administrador_institucional (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    institucion_id UUID NOT NULL REFERENCES public.institucion(id) ON DELETE CASCADE,
    nombre_completo VARCHAR(200) NOT NULL,
    correo VARCHAR(255) NOT NULL,
    codigo_pin VARCHAR(100) NOT NULL DEFAULT '',
    password_hash TEXT NOT NULL DEFAULT '',
    activo BOOLEAN NOT NULL DEFAULT true,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_admin_inst_correo UNIQUE (institucion_id, correo)
);

ALTER TABLE public.administrador_institucional
  ADD COLUMN IF NOT EXISTS password_hash TEXT NOT NULL DEFAULT '';

COMMENT ON COLUMN public.administrador_institucional.password_hash
  IS 'Hash BCrypt (cost=12) de la contraseña del administrador. Nunca almacenar en texto plano.';
