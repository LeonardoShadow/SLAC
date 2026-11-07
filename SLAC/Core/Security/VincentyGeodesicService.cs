namespace SLAC.Core.Security;

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Implementación de referencia de geodesia elipsoidal WGS-84, Haversine Gaussiano y geocercas adaptativas.
/// Diseñado como Singleton inmutable y seguro para subprocesos (Thread-Safe).
/// Soporta corrección diferencial relativa (D-GPS Relativo Capa 3) y techos estrictos de edificio (35m).
/// </summary>
public sealed class VincentyGeodesicService : IGeoCalculoService
{
    private const double Wgs84A = 6378137.0;                         // Semieje mayor en metros
    private const double Wgs84F = 1.0 / 298.257223563;              // Achatamiento elipsoidal
    private const double Wgs84B = 6356752.314245179;                // Wgs84A * (1.0 - Wgs84F)
    private const double LocalGaussianRadius = 6360748.497;         // Radio medio gaussiano UPDS Santa Cruz
    public const double AbsoluteCeilingGeofenceMeters = 28.0;       // Techo estructural estándar
    public const double StrictBuildingCeilingGeofenceMeters = 35.0; // Techo estricto absoluto de edificio/bloque (Capa 3)
    private const int MaxVincentyIterations = 200;                  // Protección contra inanición en antípodas
    private const double ConvergenceThreshold = 1e-12;              // Precisión submilimétrica

    private readonly CampusGeofenceConfig _config;
    private readonly ILogger<VincentyGeodesicService> _logger;

    public VincentyGeodesicService(
        IOptions<CampusGeofenceConfig>? configOptions = null,
        ILogger<VincentyGeodesicService>? logger = null)
    {
        _config = configOptions?.Value ?? new CampusGeofenceConfig();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<VincentyGeodesicService>.Instance;
    }

    public double CalcularDistanciaVincentyWgs84(double lat1, double lon1, double lat2, double lon2)
    {
        ValidarCoordenadas(lat1, lon1, nameof(lat1), nameof(lon1));
        ValidarCoordenadas(lat2, lon2, nameof(lat2), nameof(lon2));

        // Caso de identidad exacta
        if (lat1 == lat2 && lon1 == lon2)
            return 0.0;

        var phi1 = ToRadians(lat1);
        var phi2 = ToRadians(lat2);
        var lam1 = ToRadians(lon1);
        var lam2 = ToRadians(lon2);
        var L = lam2 - lam1;

        var tanU1 = (1.0 - Wgs84F) * Math.Tan(phi1);
        var cosU1 = 1.0 / Math.Sqrt(1.0 + (tanU1 * tanU1));
        var sinU1 = tanU1 * cosU1;

        var tanU2 = (1.0 - Wgs84F) * Math.Tan(phi2);
        var cosU2 = 1.0 / Math.Sqrt(1.0 + (tanU2 * tanU2));
        var sinU2 = tanU2 * cosU2;

        var lambda = L;
        double sinSigma = 0.0, cosSigma = 0.0, sigma = 0.0;
        double sinAlpha = 0.0, cos2Alpha = 0.0, cos2SigmaM = 0.0;
        bool converged = false;

        for (int iter = 0; iter < MaxVincentyIterations; iter++)
        {
            var sinLambda = Math.Sin(lambda);
            var cosLambda = Math.Cos(lambda);

            var term1 = cosU2 * sinLambda;
            var term2 = (cosU1 * sinU2) - (sinU1 * cosU2 * cosLambda);
            sinSigma = Math.Sqrt((term1 * term1) + (term2 * term2));

            if (sinSigma == 0.0)
                return 0.0; // Puntos coincidentes

            cosSigma = (sinU1 * sinU2) + (cosU1 * cosU2 * cosLambda);
            sigma = Math.Atan2(sinSigma, cosSigma);

            sinAlpha = (cosU1 * cosU2 * sinLambda) / sinSigma;
            cos2Alpha = 1.0 - (sinAlpha * sinAlpha);

            cos2SigmaM = (cos2Alpha != 0.0)
                ? cosSigma - ((2.0 * sinU1 * sinU2) / cos2Alpha)
                : 0.0; // Trayectoria sobre el ecuador

            var C = (Wgs84F / 16.0) * cos2Alpha * (4.0 + (Wgs84F * (4.0 - (3.0 * cos2Alpha))));
            var lambdaPrev = lambda;
            lambda = L + ((1.0 - C) * Wgs84F * sinAlpha *
                     (sigma + (C * sinSigma * (cos2SigmaM + (C * cosSigma * (-1.0 + (2.0 * cos2SigmaM * cos2SigmaM)))))));

            if (Math.Abs(lambda - lambdaPrev) < ConvergenceThreshold)
            {
                converged = true;
                break;
            }
        }

        if (!converged)
        {
            _logger.LogWarning("Fórmula inversa de Vincenty no convergió para ({Lat1},{Lon1}) -> ({Lat2},{Lon2}). Fallback seguro a Haversine Gaussiano.",
                lat1, lon1, lat2, lon2);
            return CalcularDistanciaHaversineGauss(lat1, lon1, lat2, lon2);
        }

        var u2 = cos2Alpha * ((Wgs84A * Wgs84A - Wgs84B * Wgs84B) / (Wgs84B * Wgs84B));
        var A = 1.0 + ((u2 / 16384.0) * (4096.0 + (u2 * (-768.0 + (u2 * (320.0 - (175.0 * u2)))))));
        var B = (u2 / 1024.0) * (256.0 + (u2 * (-128.0 + (u2 * (74.0 - (47.0 * u2))))));

        var deltaSigma = B * sinSigma * (cos2SigmaM + (0.25 * B * ((cosSigma * (-1.0 + (2.0 * cos2SigmaM * cos2SigmaM))) -
                         ((1.0 / 6.0) * B * cos2SigmaM * (-3.0 + (4.0 * sinSigma * sinSigma)) * (-3.0 + (4.0 * cos2SigmaM * cos2SigmaM))))));

        var s = Wgs84B * A * (sigma - deltaSigma);
        return Math.Max(0.0, s);
    }

    public double CalcularDistanciaHaversineGauss(double lat1, double lon1, double lat2, double lon2)
    {
        ValidarCoordenadas(lat1, lon1, nameof(lat1), nameof(lon1));
        ValidarCoordenadas(lat2, lon2, nameof(lat2), nameof(lon2));

        if (lat1 == lat2 && lon1 == lon2)
            return 0.0;

        var phi1 = ToRadians(lat1);
        var phi2 = ToRadians(lat2);
        var dPhi = phi2 - phi1;
        var dLam = ToRadians(lon2 - lon1);

        var sinHalfDphi = Math.Sin(dPhi / 2.0);
        var sinHalfDlam = Math.Sin(dLam / 2.0);

        var h = (sinHalfDphi * sinHalfDphi) + (Math.Cos(phi1) * Math.Cos(phi2) * (sinHalfDlam * sinHalfDlam));
        h = Math.Clamp(h, 0.0, 1.0);

        var c = 2.0 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1.0 - h));
        return LocalGaussianRadius * c;
    }

    public double CalcularRadioGeocercaAdaptativa(double sigmaDocente, double sigmaEstudiante, int radioNominalAula = 12)
    {
        if (double.IsNaN(sigmaDocente) || double.IsInfinity(sigmaDocente)) sigmaDocente = 5.0;
        if (double.IsNaN(sigmaEstudiante) || double.IsInfinity(sigmaEstudiante)) sigmaEstudiante = 8.0;

        var sigmaDocenteEff = Math.Clamp(sigmaDocente, 2.0, 8.0);
        var sigmaEstudianteEff = Math.Clamp(sigmaEstudiante, 2.0, 12.0);

        var nominal = (radioNominalAula > 0) ? radioNominalAula : 12.0;
        var radioCalculado = nominal + (0.5 * sigmaDocenteEff) + (1.0 * sigmaEstudianteEff);

        return Math.Min(AbsoluteCeilingGeofenceMeters, radioCalculado);
    }

    public double CalcularRadioGeocercaAdaptativaConTecho(double sigmaDocente, double sigmaEstudiante, int radioNominalAula = 12, double techoMaximo = StrictBuildingCeilingGeofenceMeters)
    {
        if (double.IsNaN(sigmaDocente) || double.IsInfinity(sigmaDocente)) sigmaDocente = 5.0;
        if (double.IsNaN(sigmaEstudiante) || double.IsInfinity(sigmaEstudiante)) sigmaEstudiante = 8.0;

        var sigmaDocenteEff = Math.Clamp(sigmaDocente, 2.0, 8.0);
        var sigmaEstudianteEff = Math.Clamp(sigmaEstudiante, 2.0, 12.0);

        var nominal = (radioNominalAula > 0) ? radioNominalAula : 12.0;
        var radioCalculado = nominal + (0.5 * sigmaDocenteEff) + (1.0 * sigmaEstudianteEff);

        var techoEfectivo = Math.Min(techoMaximo, StrictBuildingCeilingGeofenceMeters);
        return Math.Min(techoEfectivo, radioCalculado);
    }

    public bool EstaDentroDeRegionMetropolitana(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lat) || double.IsInfinity(lon))
            return false;

        return _config.RegionalEnclosingBox.Contiene(lat, lon);
    }

    public bool EstaDentroDeCampus(double lat, double lon, string? campusCode)
    {
        if (!EstaDentroDeRegionMetropolitana(lat, lon))
            return false;

        var campus = ObtenerCampus(campusCode);
        return campus.ContienePunto(lat, lon);
    }

    public CampusDefinition ObtenerCampus(string? campusCode)
    {
        if (!string.IsNullOrWhiteSpace(campusCode) && _config.Campuses.TryGetValue(campusCode, out var campus))
            return campus;

        if (!string.IsNullOrWhiteSpace(_config.DefaultCampusCode) && _config.Campuses.TryGetValue(_config.DefaultCampusCode, out var defaultCampus))
            return defaultCampus;

        return new CampusDefinition
        {
            Code = "UPDS_SUR",
            Name = "UPDS Campus Sur - Predeterminado",
            CenterLatitude = -17.839210,
            CenterLongitude = -63.194244,
            BoundingBox = new GeoBoundingBox(-17.843500, -17.835000, -63.199500, -63.189000)
        };
    }

    public bool ValidarPresenciaEnAula(
        double latDocente, double lonDocente,
        double latEstudiante, double lonEstudiante,
        double sigmaDocente, double sigmaEstudiante,
        int radioNominalAula,
        out double distanciaCalculada,
        out double radioPermitido)
    {
        distanciaCalculada = CalcularDistanciaVincentyWgs84(latDocente, lonDocente, latEstudiante, lonEstudiante);
        radioPermitido = CalcularRadioGeocercaAdaptativa(sigmaDocente, sigmaEstudiante, radioNominalAula);
        return distanciaCalculada <= radioPermitido;
    }

    public bool AplicarCorreccionDiferencialRelativa(
        double latDocenteMedida, double lonDocenteMedida,
        double latDocenteReferencia, double lonDocenteReferencia,
        double latEstudianteMedida, double lonEstudianteMedida,
        DateTimeOffset timestampDocente, DateTimeOffset timestampEstudiante,
        out double latEstudianteCorregida, out double lonEstudianteCorregida,
        out double sesgoAtmMetros,
        double maxVentanaSegundos = 1200.0)
    {
        ValidarCoordenadas(latDocenteMedida, lonDocenteMedida, nameof(latDocenteMedida), nameof(lonDocenteMedida));
        ValidarCoordenadas(latDocenteReferencia, lonDocenteReferencia, nameof(latDocenteReferencia), nameof(lonDocenteReferencia));
        ValidarCoordenadas(latEstudianteMedida, lonEstudianteMedida, nameof(latEstudianteMedida), nameof(lonEstudianteMedida));

        var dtSegundos = Math.Abs((timestampEstudiante - timestampDocente).TotalSeconds);
        if (dtSegundos > maxVentanaSegundos)
        {
            latEstudianteCorregida = latEstudianteMedida;
            lonEstudianteCorregida = lonEstudianteMedida;
            sesgoAtmMetros = 0.0;
            return false;
        }

        // Vector de distorsión iono-troposférica común (Punto A medido vs referencia conocida)
        var deltaLat = latDocenteMedida - latDocenteReferencia;
        var deltaLon = lonDocenteMedida - lonDocenteReferencia;
        sesgoAtmMetros = CalcularDistanciaVincentyWgs84(latDocenteReferencia, lonDocenteReferencia, latDocenteMedida, lonDocenteMedida);

        // Sanity limit: distorsión iono-troposférica local debe estar acotada (< 150m) para evitar valores atípicos
        if (sesgoAtmMetros > 150.0)
        {
            _logger.LogWarning("Sesgo diferencial anómalo ({Sesgo}m). Posible desfasaje o coordenadas corruptas. Se omite compensación diferencial.", sesgoAtmMetros);
            latEstudianteCorregida = latEstudianteMedida;
            lonEstudianteCorregida = lonEstudianteMedida;
            return false;
        }

        // Cancelación de la distorsión común en el vector del estudiante
        latEstudianteCorregida = latEstudianteMedida - deltaLat;
        lonEstudianteCorregida = lonEstudianteMedida - deltaLon;
        return true;
    }

    private static void ValidarCoordenadas(double lat, double lon, string latParamName, string lonParamName)
    {
        if (double.IsNaN(lat) || double.IsInfinity(lat))
            throw new ArgumentException($"La latitud no puede ser NaN ni Infinito.", latParamName);

        if (double.IsNaN(lon) || double.IsInfinity(lon))
            throw new ArgumentException($"La longitud no puede ser NaN ni Infinito.", lonParamName);

        if (lat < -90.0 || lat > 90.0)
            throw new ArgumentOutOfRangeException(latParamName, lat, "La latitud debe residir en el intervalo [-90.0, +90.0].");

        if (lon < -180.0 || lon > 180.0)
            throw new ArgumentOutOfRangeException(lonParamName, lon, "La longitud debe residir en el intervalo [-180.0, +180.0].");
    }

    private static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
