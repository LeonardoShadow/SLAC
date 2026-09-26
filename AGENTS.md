# Actores y Procesos del Sistema

El sistema interactúa con los siguientes actores; el código debe segmentarse para servir a cada perfil sin mezclar contextos[cite: 4]:

*   **Planificador (Background Worker):** Un proceso sin UI (Quartz.NET). Crea las sesiones del día a las 00:05, las abre a su hora, las cierra 20 min después y computa faltas automáticas[cite: 4].
*   **Docente:** Usuario autenticado. Proyecta el QR y consume reportes de solo faltas. NO interactúa con la lista durante la clase[cite: 2, 4].
*   **Estudiante:** Actor anónimo convertido a entidad verificada vía token ES256. Interfaz mínima (solo lectura y 1 escaneo)[cite: 2, 4].
*   **Administrador Institucional:** Usuario autenticado que gestiona el catálogo de aulas, feriados y docentes de su Tenant exclusivo[cite: 4].