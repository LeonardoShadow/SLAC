namespace SLAC.Core.Security;

using System;
using System.Collections.Generic;

/// <summary>
/// Representa un rectángulo delimitador geográfico (Bounding Box) con validación defensiva de bordes.
/// </summary>
public record GeoBoundingBox
{
    public double LatMin { get; init; }
    public double LatMax { get; init; }
    public double LonMin { get; init; }
    public double LonMax { get; init; }

    public GeoBoundingBox(double latMin, double latMax, double lonMin, double lonMax)
    {
        if (double.IsNaN(latMin) || double.IsNaN(latMax) || double.IsNaN(lonMin) || double.IsNaN(lonMax) ||
            double.IsInfinity(latMin) || double.IsInfinity(latMax) || double.IsInfinity(lonMin) || double.IsInfinity(lonMax))
        {
            throw new ArgumentException("Los límites del BoundingBox no pueden ser NaN ni Infinito.");
        }

        if (latMin > latMax) throw new ArgumentOutOfRangeException(nameof(latMin), "LatMin no puede ser mayor que LatMax.");
        if (lonMin > lonMax) throw new ArgumentOutOfRangeException(nameof(lonMin), "LonMin no puede ser mayor que LonMax.");

        LatMin = latMin;
        LatMax = latMax;
        LonMin = lonMin;
        LonMax = lonMax;
    }

    public bool Contiene(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lat) || double.IsInfinity(lon))
            return false;

        return lat >= LatMin && lat <= LatMax && lon >= LonMin && lon <= LonMax;
    }
}

/// <summary>
/// Vértice individual para polígonos perimetrales de campus.
/// </summary>
public record GeoPolygonPoint(double Lat, double Lon);

/// <summary>
/// Definición estructurada de un Campus universitario o sede institucional.
/// </summary>
public class CampusDefinition
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public GeoBoundingBox BoundingBox { get; set; } = new(-90.0, 90.0, -180.0, 180.0);
    public List<GeoPolygonPoint>? PolygonVertices { get; set; }
    public double BufferPerimetralMetros { get; set; } = 150.0;
    public double PrecisionGpsMaximaPermitida { get; set; } = 35.0;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Comprueba si un punto reside dentro del campus, evaluando primero el BoundingBox y
    /// posteriormente el polígono mediante el algoritmo de Ray-Casting si existen vértices definidos.
    /// </summary>
    public bool ContienePunto(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsInfinity(lat) || double.IsInfinity(lon))
            return false;

        if (!BoundingBox.Contiene(lat, lon))
            return false;

        if (PolygonVertices == null || PolygonVertices.Count < 3)
            return true; // Si no hay polígono específico, el BoundingBox es la autoridad

        return ContieneEnPoligonoRayCasting(lat, lon, PolygonVertices);
    }

    private static bool ContieneEnPoligonoRayCasting(double lat, double lon, IReadOnlyList<GeoPolygonPoint> vertices)
    {
        bool inside = false;
        int count = vertices.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var vi = vertices[i];
            var vj = vertices[j];

            if (((vi.Lat > lat) != (vj.Lat > lat)) &&
                (lon < (vj.Lon - vi.Lon) * (lat - vi.Lat) / (vj.Lat - vi.Lat) + vi.Lon))
            {
                inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>
/// Opciones de configuración para geocercas multi-campus ligadas a la sección "CampusGeofence".
/// Provee valores predeterminados seguros para permitir ejecución autónoma y pruebas sin dependencias externas.
/// </summary>
public class CampusGeofenceConfig
{
    public const string SectionName = "CampusGeofence";

    public string DefaultCampusCode { get; set; } = "UPDS_SUR";

    /// <summary>
    /// Rectángulo delimitador del Área Metropolitana de Santa Cruz de la Sierra.
    /// Permite descartar de forma Fail-Fast peticiones fuera de la región.
    /// </summary>
    public GeoBoundingBox RegionalEnclosingBox { get; set; } = new(-17.950000, -17.650000, -63.300000, -63.050000);

    public Dictionary<string, CampusDefinition> Campuses { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UPDS_SUR"] = new CampusDefinition
        {
            Code = "UPDS_SUR",
            Name = "UPDS Campus Sur - Av. Banzer y Los Cusis",
            CenterLatitude = -17.839210,
            CenterLongitude = -63.194244,
            BufferPerimetralMetros = 150.0,
            PrecisionGpsMaximaPermitida = 35.0,
            IsActive = true,
            BoundingBox = new GeoBoundingBox(-17.843500, -17.835000, -63.199500, -63.189000)
        },
        ["UPDS_CENTRAL"] = new CampusDefinition
        {
            Code = "UPDS_CENTRAL",
            Name = "UPDS Campus Central - Av. Paraguá y 4to Anillo",
            CenterLatitude = -17.776200,
            CenterLongitude = -63.195100,
            BufferPerimetralMetros = 150.0,
            PrecisionGpsMaximaPermitida = 35.0,
            IsActive = true,
            BoundingBox = new GeoBoundingBox(-17.781000, -17.771000, -63.200000, -63.190000)
        }
    };
}
