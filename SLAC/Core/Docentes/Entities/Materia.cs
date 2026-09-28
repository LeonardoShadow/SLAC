namespace SLAC.Core.Docentes.Entities;

/// <summary>
/// Representa una materia académica publicada en SLAC.
/// Cumple estrictamente con la regla de negocio RN-01:
/// - Días de clase válidos exclusivamente de Lunes a Viernes (L, M, X, J, V).
/// - Un único horario de inicio predefinido (no fraccionado).
/// </summary>
public class Materia
{
    public static readonly string[] DiasValidos = ["L", "M", "X", "J", "V"];

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitucionId { get; set; }
    public Guid DocenteId { get; set; }
    public Guid PeriodoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;
    public Guid EspacioId { get; set; }
    public TimeSpan HoraInicio { get; set; }
    public string Dias { get; set; } = "L,M,V";
    public string Estado { get; set; } = "activa"; // 'activa', 'archivada'
    public DateTimeOffset CreadoEn { get; set; } = DateTimeOffset.UtcNow;

    // Propiedades de navegación / presentación
    public string? NombreEspacio { get; set; }
    public string? NombrePeriodo { get; set; }
    public string? NombreDocente { get; set; }

    /// <summary>
    /// Valida que la materia cumpla las reglas de negocio RN-01 del SRS:
    /// Días válidos Lunes a Viernes, código y nombre no vacíos, hora válida.
    /// </summary>
    public (bool EsValida, string? Error) ValidarReglasNegocio()
    {
        if (string.IsNullOrWhiteSpace(Codigo))
            return (false, "El código de la materia es requerido.");

        if (string.IsNullOrWhiteSpace(Nombre))
            return (false, "El nombre de la materia es requerido.");

        if (string.IsNullOrWhiteSpace(Grupo))
            return (false, "El grupo de la materia es requerido.");

        if (EspacioId == Guid.Empty)
            return (false, "Debe asignar un espacio/aula físico a la materia.");

        if (PeriodoId == Guid.Empty)
            return (false, "Debe asignar un periodo académico.");

        if (DocenteId == Guid.Empty)
            return (false, "Debe asignar un docente a la materia.");

        if (HoraInicio < TimeSpan.Zero || HoraInicio >= TimeSpan.FromHours(24))
            return (false, "La hora de inicio debe ser válida entre las 00:00 y las 23:59.");

        var diasList = ObtenerListaDias();
        if (diasList.Length == 0)
            return (false, "Debe seleccionar al menos un día de clase.");

        foreach (var dia in diasList)
        {
            if (!DiasValidos.Contains(dia))
            {
                return (false, $"El día '{dia}' no es permitido por la regla RN-01. Solo se admiten días de lunes a viernes (L, M, X, J, V).");
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Devuelve los días de clase descompuestos en un array limpio de siglas.
    /// </summary>
    public string[] ObtenerListaDias()
    {
        if (string.IsNullOrWhiteSpace(Dias)) return [];
        return Dias.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Establece los días formateados a partir de un IEnumerable de siglas.
    /// </summary>
    public void EstablecerDias(IEnumerable<string> diasSeleccionados)
    {
        Dias = string.Join(",", diasSeleccionados.Select(d => d.Trim().ToUpperInvariant()));
    }

    /// <summary>
    /// Convierte la lista de siglas a nombres legibles en español (Lunes, Martes, etc.).
    /// </summary>
    public string ObtenerDiasLegibles()
    {
        var siglas = ObtenerListaDias();
        var nombres = siglas.Select(s => s switch
        {
            "L" => "Lunes",
            "M" => "Martes",
            "X" => "Miércoles",
            "J" => "Jueves",
            "V" => "Viernes",
            _ => s
        });
        return string.Join(", ", nombres);
    }
}
