namespace SLAC.Features.Attendance.Services;

/// <summary>
/// Servicio de generación de códigos QR vectoriales (SVG) de alta fidelidad para pantallas de proyección.
/// </summary>
public interface IQrCodeGeneratorService
{
    /// <summary>
    /// Genera un SVG del código QR a partir del contenido provisto.
    /// </summary>
    string GenerateSvg(string content, int pixelsPerModule = 10, string darkColorHex = "#000000", string lightColorHex = "#FFFFFF");
}
