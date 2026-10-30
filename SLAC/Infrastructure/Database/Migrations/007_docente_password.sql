-- 007_docente_password.sql
-- Migración para añadir soporte de contraseña segura con BCrypt a los docentes.

ALTER TABLE public.docente
  ADD COLUMN IF NOT EXISTS password_hash TEXT NOT NULL DEFAULT '';

COMMENT ON COLUMN public.docente.password_hash
  IS 'Hash BCrypt (cost=12) de la contraseña del docente para inicio de sesión seguro.';
