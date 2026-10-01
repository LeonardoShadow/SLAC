namespace SLAC.Core.Institucional.Entities;

public class AdministradorInstitucional
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string CodigoPin { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;
}
