namespace SLAC.Core.Security;

/// <summary>
/// Contrato del servicio geodésico y de geocercas de alta fidelidad para el modo GPS Alpha.
/// Soporta corrección diferencial relativa (D-GPS Relativo Capa 3) y techos estrictos de edificio.
/// </summary>
public interface IGeoCalculoService
{
    /// <summary>
    /// Techo estricto absoluto de geocerca en metros (Capa 3: 35.0m para garantizar aislamiento de bloque/edificio).
    /// </summary>
    public const double StrictBuildingCeilingGeofenceMeters = 35.0;

    /// <summary>
    /// Calcula la distancia geodésica elipsoidal exacta entre dos coordenadas WGS-84 mediante la fórmula inversa de Vincenty (1975).
    /// </summary>
    /// <param name="lat1">Latitud punto 1 en grados decimales [-90, 90]</param>
    /// <param name="lon1">Longitud punto 1 en grados decimales [-180, 180]</param>
    /// <param name="lat2">Latitud punto 2 en grados decimales [-90, 90]</param>
    /// <param name="lon2">Longitud punto 2 en grados decimales [-180, 180]</param>
    /// <returns>Distancia elipsoidal en metros con resolución milimétrica.</returns>
    double CalcularDistanciaVincentyWgs84(double lat1, double lon1, double lat2, double lon2);

    /// <summary>
    /// Calcula la distancia geodésica utilizando la fórmula de Haversine escalada con el radio de curvatura gaussiano local (6,360,748.497m).
    /// </summary>
    double CalcularDistanciaHaversineGauss(double lat1, double lon1, double lat2, double lon2);

    /// <summary>
    /// Calcula el radio adaptativo de geocerca del aula considerando la incertidumbre instrumental del docente y del alumno,
    /// con acotamiento estándar a 28.0 metros (Zero-Trust Clamping).
    /// </summary>
    double CalcularRadioGeocercaAdaptativa(double sigmaDocente, double sigmaEstudiante, int radioNominalAula = 12);

    /// <summary>
    /// Calcula el radio adaptativo de geocerca del aula aplicando un techo máximo explícito acotado estrictamente a 35.0 metros.
    /// </summary>
    double CalcularRadioGeocercaAdaptativaConTecho(double sigmaDocente, double sigmaEstudiante, int radioNominalAula = 12, double techoMaximo = StrictBuildingCeilingGeofenceMeters);

    /// <summary>
    /// Determina si una coordenada reside dentro del rectángulo envolvente metropolitano regional de Santa Cruz.
    /// </summary>
    bool EstaDentroDeRegionMetropolitana(double lat, double lon);

    /// <summary>
    /// Determina si una coordenada reside dentro de los límites autorizados del campus especificado.
    /// </summary>
    bool EstaDentroDeCampus(double lat, double lon, string? campusCode);

    /// <summary>
    /// Obtiene la definición estructurada de un campus por su código (case-insensitive). Devuelve el campus por defecto si es nulo o desconocido.
    /// </summary>
    CampusDefinition ObtenerCampus(string? campusCode);

    /// <summary>
    /// Valida integralmente si un estudiante se encuentra físicamente en el aula respecto a la posición del docente.
    /// </summary>
    bool ValidarPresenciaEnAula(
        double latDocente, double lonDocente,
        double latEstudiante, double lonEstudiante,
        double sigmaDocente, double sigmaEstudiante,
        int radioNominalAula,
        out double distanciaCalculada,
        out double radioPermitido);

    /// <summary>
    /// Aplica la corrección diferencial relativa (D-GPS Relativo Capa 3): cuando se dispone del vector docente (Punto A)
    /// y vector estudiante (Punto B) en la misma ventana de tiempo, cancela la distorsión iono-troposférica común.
    /// </summary>
    bool AplicarCorreccionDiferencialRelativa(
        double latDocenteMedida, double lonDocenteMedida,
        double latDocenteReferencia, double lonDocenteReferencia,
        double latEstudianteMedida, double lonEstudianteMedida,
        DateTimeOffset timestampDocente, DateTimeOffset timestampEstudiante,
        out double latEstudianteCorregida, out double lonEstudianteCorregida,
        out double sesgoAtmMetros,
        double maxVentanaSegundos = 1200.0);
}
