-- =============================================================================
-- SLAC - Migración 004: Políticas de Row Level Security (RLS) Multi-Tenant
-- =============================================================================

-- Función helper para obtener las instituciones del usuario autenticado actual
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

-- Función helper para validar si el usuario autenticado tiene un rol específico en una institución
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

-- Habilitar RLS en todas las tablas
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

-- 1. Políticas para institucion
CREATE POLICY rls_institucion_select ON public.institucion
    FOR SELECT TO authenticated
    USING (id IN (SELECT public.slac_current_user_instituciones()));

-- 2. Políticas para usuario_institucion
CREATE POLICY rls_usuario_institucion_select ON public.usuario_institucion
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

-- 3. Políticas para docente
CREATE POLICY rls_docente_select ON public.docente
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

CREATE POLICY rls_docente_admin ON public.docente
    FOR ALL TO authenticated
    USING (public.slac_user_has_role(institucion_id, 'admin_institucional'))
    WITH CHECK (public.slac_user_has_role(institucion_id, 'admin_institucional'));

-- 4. Políticas para estudiante
CREATE POLICY rls_estudiante_select ON public.estudiante
    FOR SELECT TO authenticated
    USING (institucion_id IN (SELECT public.slac_current_user_instituciones()));

-- 5. Políticas para espacio, periodo y dias no lectivos
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

-- 6. Políticas para materia
-- Docente solo ve o edita sus materias asignadas o admin ve todas de la institución
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

-- 7. Políticas para lista_asistencia y asistencia_detalle
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

-- 8. Políticas para auditoría
CREATE POLICY rls_auditoria_select ON public.auditoria
    FOR SELECT TO authenticated
    USING (
        public.slac_user_has_role(institucion_id, 'admin_institucional')
    );
