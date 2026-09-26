# Reglas de Negocio (Business Rules)

Al escribir lógica para SLAC, se deben respetar las siguientes reglas inmutables:

1.  **Vigencia del QR:** La ventana de asistencia es estrictamente [hora de inicio, hora de inicio + 20 minutos][cite: 4]. Fuera de este tiempo, el sistema debe devolver un rechazo[cite: 2].
2.  **Rotación del QR:** El componente visual del QR (token) se renueva cada 15 segundos dentro de la ventana de asistencia para evitar reenvíos por fotos[cite: 4].
3.  **Unicidad de Asistencia:** Un estudiante tiene como máximo un registro de detalle por sesión[cite: 4]. Inserciones concurrentes deben resolverse con `ON CONFLICT DO NOTHING`[cite: 4].
4.  **Suscripción Tardía:** Si un estudiante se suscribe después de la primera clase, el sistema debe registrarle automáticamente faltas en todas las sesiones previamente cerradas de esa materia[cite: 2, 4].
5.  **Exclusividad de Dispositivo:** Un dispositivo solo puede estar vinculado a un estudiante por institución[cite: 4].
6.  **Cierre Idempotente:** El Worker que calcula faltas al cerrar la sesión de 20 minutos debe ser idempotente[cite: 4].