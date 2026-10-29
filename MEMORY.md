# Memoria de Proyecto SLAC (Estado Actual y Contexto)

## 1. Estado General del Proyecto
- **Sistema:** SLAC (Sistema de Lista de Asistencia Criptográfica) para UPDS.
- **Tecnología:** .NET 9, Blazor Interactive Server, MudBlazor, Supabase (PostgreSQL), Redis (ephemeral cache/pub-sub), SignalR.
- **Hosting de Producción:** MonsterASP.NET (`http://slac-asistencia.runasp.net/`).
- **Pruebas Unitarias:** 48 pruebas superadas en `SLAC.Tests` (100% passing).
- **Rama Git:** `Gravedad_IDE`.

---

## 2. Componentes Clave y Decisiones Implementadas

### A. Proyección Dual y Ventana Limpia para Proyectores (`PantallaProyeccionPage.razor` y `ProyeccionPage.razor`)
- **Ventana Limpia para Proyectores (`/proyeccion/{materiaId}/pantalla`):**
  - Vista independiente con `EmptyLayout` (sin menús, barras de herramientas ni controles).
  - Muestra exclusivamente el código QR vectorial SVG centrado y de alto contraste con temporizador/barra de rotación o modo QR fijo.
  - Sincronización en tiempo real vía SignalR (`/hubs/attendance`) con la consola del docente.
- **Consola del Docente (`ProyeccionPage.razor`):**
  - Botón prominente **"PROYECTAR YA (PANTALLA COMPLETA)"** que despliega un overlay de pantalla completa con un botón circular **"X"** de cierre que no altera el estado de la sesión ni de la clase.
  - Botón **"📺 Abrir en Segunda Pantalla / Proyector (Ventana Limpia)"** mediante script JS `slacOpenCleanProjector` para separar la vista del proyector de la consola de control.
  - Lista de estudiantes en vivo con contador reactivo y acciones rápidas: "Suspender Clase (Emergencia)", "Cerrar Asistencia Ahora", y "Revinculación de Dispositivos".
  - **Experiencia Móvil Optimizada:** Totalmente responsivo en dispositivos móviles (< 600px y < 960px), con scroll natural, objetivos táctiles de más de 44px y adaptación del feed de asistencia.

### B. Soporte Completo del Día Sábado y Ajuste de Predeterminados
- **Días Predeterminados:** Al crear o editar materias (`MateriaDialog.razor` y `MateriasAdminPage.razor`), se seleccionan de forma predeterminada todos los días entre semana: Lunes a Viernes (`L,M,X,J,V`).
- **Sábado Seleccionable (`S`):** Se agregó soporte completo para el día Sábado (`"S"`):
  - Entidad `Materia.cs`: `DiasValidos = ["L", "M", "X", "J", "V", "S"]`, mapeo legible `"S" => "Sábado"`.
  - Validación de regla de negocio RN-01 actualizada para aceptar Sábados.
  - Planificador de fondo `GeneracionDiariaJob.cs`: Soporta `DayOfWeek.Saturday => "S"`, ignorando únicamente los Domingos.
  - Tests unitarios actualizados y validados para materias sabatinas.

### C. Limpieza Integral de Datos Ficticios y Predeterminados
- Se eliminaron por completo las auto-semillas de datos demo en el código (`DocenteMateriasPage.razor` y `LoginPage.razor`): "Aula Magna 101", "Lab Computación 2", "Gestión I-2026", y docente demo "Jimmy Requena".
- Se migraron las sesiones históricas en Supabase y se eliminaron los registros de prueba huérfanos.
- El catálogo activo de aulas físicas en Supabase se conserva con aulas reales (`Lab-01`, `Lab-03`).

### D. Seguridad Rigurosa y Resiliencia en Tiempo Real (Fail-Fast)
- **Eliminación de Pérdida Silenciosa de Datos:** Anteriormente, cuando el proyecto Supabase entraba en reposo (Free Tier tras inactividad), las excepciones se silenciaban en `_fallbackStore` en RAM volátil, borrándose con el reciclaje del pool de IIS.
- **Fail-Fast en Producción:** `SupabaseMateriaRepository` y `SupabaseEspacioRepository` ahora fallan de inmediato arrojando excepciones claras si Supabase no responde, aislando clientes ficticios de pruebas unitarias mediante `IsDummyClient`.
- **Monitor de Salud en Vivo (`DatabaseHealthService`):**
  - Chequeo proactivo hacia Supabase REST (`IDatabaseHealthService`).
  - Alerta visual roja destacada en `MainLayout.razor` cuando la base de datos se encuentra fuera de línea o en reposo, con botón "Reintentar Conexión".

### E. Criptografía y Persistencia de Dispositivos (`KeyManager.cs`)
- Clave maestra fija ECDSA P-256 (`DefaultMasterPem` en `KeyManager.cs` y `appsettings.json` con kid `slac-master-k1`).
- **Garantía:** Los teléfonos móviles vinculados conservan su cookie `slac_device_cred` válida indefinidamente a través de nuevos despliegues del archivo `slac_publish.zip`.

---

## 3. Despliegue
- Paquete de publicación listo: `slac_publish.zip` en la raíz del proyecto.
- Para publicar en el hosting: extraer el contenido en la raíz de la aplicación en MonsterASP.NET.

---

## 4. Instrucciones para Continuar
Cuando retomes la sesión o abras el proyecto:
1. **Contexto Inmediato:** Este archivo `MEMORY.md`, junto con `AGENTS.md`, `DATABASE.md` y `credenciales.txt`, contiene todo el estado del sistema.
2. **Base de Datos y Configuración:**
   - Supabase URL y llaves están en `SLAC/appsettings.json` y el esquema completo en `DATABASE.md`.
   - La clave criptográfica maestra (`slac-master-k1`) NO debe cambiarse nunca para preservar las cookies en los celulares de los estudiantes.
3. **Pruebas y Estado:** Todos los 48 tests unitarios pasan al 100% (`dotnet test SLAC.Tests/SLAC.Tests.csproj`).
