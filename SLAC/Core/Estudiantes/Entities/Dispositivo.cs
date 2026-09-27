namespace SLAC.Core.Estudiantes.Entities;

/// <summary>
/// Representa la credencial criptográfica de un dispositivo físico o navegador del estudiante.
/// No almacena ningún dato de identificación personal (PII), solo el hash SHA-256 del jti emitido (SECURITY.md).
/// </summary>
public class Dispositivo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string JtiHash { get; set; } = string.Empty;
    public string Kid { get; set; } = string.Empty;
    public DateTimeOffset EmitidoEn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UltimoUsoEn { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevocadoEn { get; set; }
    public string? AgenteResumen { get; set; }

    public bool EstaRevocado => RevocadoEn.HasValue;
}
