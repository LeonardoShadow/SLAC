# Memoria de Proyecto SLAC (Estado Actual y Contexto)

## 1. Estado General del Proyecto
- **Sistema:** SLAC (Sistema de Lista de Asistencia Criptográfica) para UPDS.
- **Tecnología:** .NET 9, Blazor Interactive Server, MudBlazor, Supabase (PostgreSQL), Redis (ephemeral cache/pub-sub), SignalR.
- **Hosting de Producción:** MonsterASP.NET (`http://slac-asistencia.runasp.net/`).
- **Pruebas Unitarias:** 47 pruebas superadas en `SLAC.Tests` (100% passing).

---

## 2. Componentes Clave y Decisiones Implementadas

### A. Proyección de Aula y QR (`ProyeccionPage.razor`)
- **Rotación Dinámica:** Genera tokens firmados ES256 cada 15 segundos con validación de ventana de sesión (20 min por defecto).
- **Modo QR Único (Sin Rotación):** Permite al docente activar "QR Único" para proyectar un solo código fijo durante toda la clase (`rotacionIndex: -1`). `TokenService` valida la ventana de la sesión sin exigir rotación de 15s.
- **Modos de Validación:**
  1. *Solo Wi-Fi / QR:* Recomendado para pruebas o redes locales sin HTTPS. Los estudiantes registran al instante.
  2. *GPS Obligatorio:* Exige geolocalización satelital dentro del radio de tolerancia configurado (30m, 100m, 300m, etc.) contra las coordenadas del aula calibradas por el docente o fijadas en el catálogo.

### B. Criptografía y Persistencia de Dispositivos (`KeyManager.cs`)
- Clave maestra fija ECDSA P-256 (`DefaultMasterPem` en `KeyManager.cs` y `appsettings.json` con kid `slac-master-k1`).
- **Garantía:** Los teléfonos móviles vinculados conservan su cookie `slac_device_cred` válida indefinidamente a través de nuevos despliegues del archivo `slac_publish.zip`. No requiere revinculación tras actualizaciones.

### C. Gestión de Revinculación y Residuos Huérfanos
- Si un estudiante cambia de dispositivo, se crea una solicitud de revinculación autorizable por el docente en pantalla.
- **Auto-Resolución:** Cuando un estudiante con dispositivo reconocido marca asistencia con éxito, cualquier solicitud pendiente huérfana de esa materia se marca automáticamente como usada en Supabase y memoria (`SupabaseRevinculacionRepository.cs`), evitando alertas fantasma.
- **Reinicio para Pruebas:** El botón de reinicio en proyección borra asistencias de prueba y limpia solicitudes de revinculación residuales.

### D. Multi-Tenant y Reportes
- Administrador institucional con gestión de aulas, docentes, materias y feriados.
- Reportes docentes de faltas consecutivas (3 faltas = riesgo académico), exportación consolidada en Excel y PDF.

---

## 3. Despliegue
- Paquete de publicación listo: `slac_publish.zip` en la raíz del proyecto.
- Para publicar en el hosting: extraer el contenido en la raíz de la aplicación en MonsterASP.NET.

---

## 4. Instrucciones para Antigravity en la Laptop (Continuación de Trabajo)
Cuando abras Antigravity en la laptop:
1. **Contexto Inmediato:** No tienes que adivinar nada. Este archivo `MEMORY.md`, junto con `AGENTS.md`, `DATABASE.md` y `credenciales.txt`, contiene todo el estado del sistema.
2. **Base de Datos y Configuración:**
   - Supabase URL y llaves están en `SLAC/appsettings.json` y el esquema completo en `DATABASE.md`.
   - La clave criptográfica maestra (`slac-master-k1`) NO debe cambiarse nunca para preservar las cookies en los celulares de los estudiantes.
3. **Última Función Implementada y Probada:**
   - **QR Único (Sin Rotación):** Botón en proyección para congelar el QR durante toda la clase sin rotar cada 15 segundos (`rotacionIndex: -1`).
   - Todos los 47 tests unitarios pasan al 100% (`dotnet test ..\SLAC.Tests\SLAC.Tests.csproj`).
   - El archivo `slac_publish.zip` ya contiene esta compilación lista para desplegar.
4. **Para Iniciar en la Laptop:** Simplemente dile al chat: `"Continuemos según MEMORY.md"`.