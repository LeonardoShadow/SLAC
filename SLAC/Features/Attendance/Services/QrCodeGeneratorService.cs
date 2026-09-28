using QRCoder;

namespace SLAC.Features.Attendance.Services;

/// <summary>
/// Generador de códigos QR vectoriales en formato SVG usando QRCoder.
/// Diseñado para proyecciones de alta definición legibles desde el fondo del aula.
/// </summary>
public class QrCodeGeneratorService : IQrCodeGeneratorService
{
    public string GenerateSvg(string content, int pixelsPerModule = 10, string darkColorHex = "#000000", string lightColorHex = "#FFFFFF")
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var qrCode = new SvgQRCode(qrCodeData);
        return qrCode.GetGraphic(pixelsPerModule, darkColorHex, lightColorHex, drawQuietZones: true);
    }
}
