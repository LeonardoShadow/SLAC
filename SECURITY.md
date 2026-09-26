# Directrices de Seguridad y Autenticación (SLAC)

## Aislamiento de Datos (Multi-Tenant)
*   **Row Level Security (RLS):** Es OBLIGATORIO habilitar políticas RLS en todas las tablas de Supabase para garantizar que administradores y docentes solo accedan a los datos donde el `institucion_id` coincida con su institución[cite: 4].

## Identificación Transparente (Estudiantes)
*   **Cero Contraseñas:** Los estudiantes no tienen usuario ni contraseña[cite: 2].
*   **Firma Criptográfica:** La identidad del estudiante se valida mediante una credencial de dispositivo y un token de sesión, ambos firmados con ECDSA P-256 (ES256) utilizando una clave privada que NUNCA debe salir del servidor[cite: 4].
*   **Privacidad de Datos:** La credencial del dispositivo almacenada en el cliente NO DEBE contener datos personales del estudiante[cite: 4].
*   **Almacenamiento Seguro:** La credencial se debe enviar como una cookie `HttpOnly`, `Secure` y `SameSite=Lax`[cite: 4].

## Seguridad Operativa
*   **Rotación de Secretos:** Se debe usar un identificador `kid` para facilitar la rotación de claves criptográficas[cite: 4].
*   **Limitación de Tasa:** Se debe implementar un límite de peticiones (Rate Limiting) por IP y dispositivo en los endpoints de escaneo (`/a/*`)[cite: 4].