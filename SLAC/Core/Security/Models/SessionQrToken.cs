namespace SLAC.Core.Security.Models;

/// <summary>
/// Modelo del Token firmado incluido dentro del código QR proyectado en el aula (SRS 4.4).
/// No contiene ningún dato personal del estudiante ni del docente.
/// </summary>
public class SessionQrToken
{
    /// <summary>
    /// ID de la sesión/lista de asistencia (UUID).
    /// </summary>
    public Guid SesionId { get; set; }

    /// <summary>
    /// ID de la institución a la que pertenece la clase (UUID).
    /// </summary>
    public Guid InstitucionId { get; set; }

    /// <summary>
    /// Marca de tiempo UTC en que inició la ventana de asistencia (Ticks o Unix Seconds).
    /// </summary>
    public long InicioVigenciaUnix { get; set; }

    /// <summary>
    /// Marca de tiempo UTC en que expira la ventana completa de la clase (20 min por defecto).
    /// </summary>
    public long VencimientoUnix { get; set; }

    /// <summary>
    /// Contador secuencial del ciclo de rotación (cada 15 segundos).
    /// </summary>
    public int RotacionIndex { get; set; }

    /// <summary>
    /// Valor aleatorio criptográfico para evitar ataques de repetición o predicción.
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// Identificador de la clave criptográfica utilizada para firmar el token.
    /// </summary>
    public string Kid { get; set; } = string.Empty;
}
