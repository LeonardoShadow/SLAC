# SLAC - Sistema de Lista de Asistencia a Clases

## Descripción del Proyecto
SLAC es una aplicación web multi-institución diseñada para reemplazar el pase de lista manual mediante la generación de un código QR diario y tokenizado[cite: 2, 4]. Está dirigido al mercado educativo (universidades y colegios)[cite: 2]. El sistema permite que los estudiantes registren su asistencia en segundos escaneando un QR válido por 20 minutos, identificándolos mediante una credencial de dispositivo sin requerir usuario ni contraseña[cite: 2, 4].

## Arquitectura y Stack Tecnológico
*   **Frontend y Backend Unificado:** Blazor Web App (.NET 9)[cite: 4].
    *   **Páginas de estudiante:** Renderizado estático en servidor (SSR) para máxima ligereza y velocidad[cite: 4].
    *   **Portal docente/admin:** Renderizado interactivo en servidor (Interactive Server) con MudBlazor[cite: 4].
*   **Base de Datos y Autenticación:** Supabase (PostgreSQL 15+ y Supabase Auth)[cite: 4].
*   **Estado Efímero y Tiempo Real:** Redis 7 Pub/Sub (SignalR backplane) para contadores en vivo y expiración de sesiones QR[cite: 4].
*   **Tareas en Segundo Plano:** Planificador implementado con Worker Service (.NET) y Quartz.NET para apertura/cierre de sesiones[cite: 4].

## Estructura Multi-Tenant
El sistema aloja a múltiples instituciones con datos completamente aislados[cite: 2]. Todas las tablas de negocio incluyen la columna `institucion_id`[cite: 4].