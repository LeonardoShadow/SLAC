# Notas y Dependencias Críticas (Memos)

*   **Sincronización de Tiempo:** La integridad del sistema depende absolutamente del reloj del servidor (NTP). Nunca se debe confiar en la hora del dispositivo del cliente para calcular llegadas tarde o ventanas de validez[cite: 4].
*   **Retención de Cookies (Riesgo RT1):** Si el navegador móvil del usuario borra las cookies (modo incógnito o políticas estrictas), la credencial se pierde. Esto está mitigado operativamente permitiendo la "Revinculación" manual autorizada por el docente[cite: 4].
*   **Rendimiento (Riesgo RT3):** Se espera una ráfaga masiva de escaneos concurrentes en el minuto cero de la clase. Validar primero la sesión en Redis antes de realizar transacciones en PostgreSQL[cite: 4].
*   **Preguntas Abiertas (Pendientes de Definición):** Validar si se usará verificación de geolocalización o Wi-Fi (P3), y si la ventana de 20 minutos podrá ser extendida por el docente (P4)[cite: 4].