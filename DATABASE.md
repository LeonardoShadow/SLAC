# Documentación de Base de Datos - SLAC

## 1. Visión General
Este documento describe el modelo de datos, la estructura y las reglas de negocio a nivel de base de datos para el Sistema de Lista de Asistencia a Clases (SLAC)[cite: 6, 7].

*   **Motor:** PostgreSQL 15+[cite: 6, 7]
*   **Proveedor:** Supabase[cite: 6, 7]
*   **Arquitectura:** Multi-Tenant (Múltiples instituciones en una sola base de datos, aisladas lógicamente)[cite: 6, 7].

## 2. Aislamiento de Datos (Multi-Tenant) y Seguridad
El principio de diseño fundamental es el aislamiento estricto de datos por institución.
*   **Columna Tenant:** TODAS las tablas de negocio incluyen obligatoriamente la columna `institucion_id` (UUID)[cite: 6, 7].
*   **Row Level Security (RLS):** Se DEBE habilitar RLS en PostgreSQL para todas las tablas. Las políticas asegurarán que un usuario (autenticado vía Supabase Auth) solo pueda hacer SELECT/INSERT/UPDATE/DELETE sobre las filas donde `institucion_id` coincida con las instituciones a las que está vinculado en la tabla `usuario_institucion`[cite: 6, 7].
*   **Autenticación:** La tabla `auth.users` (gestionada por Supabase) manejará el acceso de Docentes y Administradores Institucionales. Los estudiantes NO tienen cuenta en `auth.users`; su identificación es mediante credenciales ES256 gestionadas en el backend[cite: 7].

## 3. Modelo de Datos Lógico y Tablas Principales

La estructura se divide en entidades de configuración y entidades transaccionales[cite: 6, 7].

### 3.1 Entidades de Configuración e Identidad
*   **`institucion`**: La tabla Tenant raíz. Define reglas globales por cliente (ej. zona horaria, duración de ventana de asistencia, etc.).
*   **`usuario_institucion`**: Tabla intermedia que vincula cuentas de `auth.users` con una o varias instituciones y define su rol (Docente, Administrador).
*   **`docente`**: Información de perfil del docente, vinculado a un `user_id` de autenticación.
*   **`estudiante`**: Perfil del estudiante (código, nombres, correo). La unicidad está garantizada por la combinación de `institucion_id` y `codigo`[cite: 6, 7].
*   **`dispositivo`**: Almacena las credenciales emitidas (sin datos personales). Contiene el hash del JTI y la marca de revocación[cite: 6].
*   **`dispositivo_estudiante`**: Vincula un dispositivo a un estudiante específico dentro de una institución[cite: 6].

### 3.2 Entidades Académicas
*   **`espacio`**: Aulas y laboratorios disponibles por institución[cite: 6].
*   **`periodo_academico`**: Semestres o bimestres, con fecha de inicio y fin[cite: 6].
*   **`dia_no_lectivo`**: Feriados institucionales[cite: 6].
*   **`materia`**: Creada por el docente, define el horario, días (L-V), y espacio asignado[cite: 6].
*   **`suscripcion`**: Vincula a un estudiante con una materia tras su primer escaneo[cite: 6]. Garantiza que el estudiante pertenece a la clase[cite: 6].

### 3.3 Entidades Transaccionales (El "Core" del Negocio)
*   **`lista_asistencia`**: Tabla maestra que representa una clase en un día específico. Se crea automáticamente por el planificador[cite: 6]. 
    *   **Clave Única:** `(materia_id, fecha)`[cite: 6].
*   **`asistencia_detalle`**: Registra la asistencia o falta de cada estudiante para una `lista_asistencia` concreta[cite: 6].
    *   **Clave Única:** `(lista_id, estudiante_id)`[cite: 6].
    *   *Nota de rendimiento:* Las inserciones concurrentes al inicio de clase deben manejar el conflicto usando `ON CONFLICT DO NOTHING`[cite: 6].
*   **`auditoria`**: Tabla de solo inserción para registrar eventos sensibles (ej. cierres de sesión, cambios de falta a presente)[cite: 6].

## 4. Reglas de Negocio en Base de Datos (Restricciones y Triggers)
*   **Integridad Referencial:** Todas las llaves foráneas deben usar `ON DELETE CASCADE` hacia su institución correspondiente para asegurar que al eliminar un Tenant, se limpie toda su información.
*   **Estados Permitidos:** 
    *   En `asistencia_detalle`, el `estado` debe estar restringido a `('Presente', 'Falta')`[cite: 6].
    *   En `asistencia_detalle`, el `origen` debe estar restringido a `('QR', 'Cierre', 'Suscripción tardía', 'Corrección')`[cite: 6].
*   **Valores por Defecto Cero:** En `lista_asistencia`, los campos `total_suscritos`, `total_presentes` y `total_faltas` deben iniciar en 0[cite: 6].

## 5. Índices Críticos para el Rendimiento
*   **Reportes y Filtros:** Se debe crear un índice compuesto sobre `(institucion_id, lista_id)` en la tabla `asistencia_detalle` para optimizar las consultas del docente y la aplicación de políticas RLS[cite: 6].