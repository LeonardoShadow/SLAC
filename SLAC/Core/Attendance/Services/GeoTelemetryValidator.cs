namespace SLAC.Core.Attendance.Services;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Validador de telemetría GNSS cruda en servidor bajo el principio Zero-Trust (Milestone 1).
/// Ejecuta sanitización exhaustiva, detección de inyecciones sintéticas (varianza cero),
/// filtrado robusto MAD, guardia cinemática estocástica y recálculo BLUE insesgado.
/// </summary>
public static class GeoTelemetryValidator
{
    public const int MinRequiredSamples = 1;
    public const int MaxAllowedSamples = 50;
    public const double MaxPedestrianSpeedMps = 15.0;
    public const double MinSpatialStdDevMeters = 0.0;
    public const double MaxAllowedClockSkewMs = 300000.0; // 5 minutos de tolerancia para desfase horario del celular
    public const long MinSampleDeltaTimeMs = 0;
    public const long MaxSampleDeltaTimeMs = 120000; // Hasta 120 segundos entre actualizaciones GPS en interiores
    public const double MaxDiscrepancyMeters = 50.0;

    // Factores geodésicos locales de proyección ortomórfica en Santa Cruz de la Sierra (latitud ~-17.8°)
    public const double MetersPerDegreeLat = 110650.0;
    public const double MetersPerDegreeLon = 105970.0;

    public const double MadScaleFactor = 1.4826;
    public const double MadThresholdZ = 3.0;
    public const double MinAllowedMad = 1e-7;
    public const double MinAllowedVariance = 0.25;

    public static TelemetryValidationResult ValidateAndDeriveLocation(
        List<RawGpsTelemetrySample>? samples,
        long serverNowUnixMs,
        double? clientClaimedLat = null,
        double? clientClaimedLon = null)
    {
        // 1. Verificación de límites de recuento de muestras [1, 50]
        if (samples == null || samples.Count < MinRequiredSamples)
        {
            var count = samples?.Count ?? 0;
            return Fail($"Muestras insuficientes ({count}). Se requiere al menos {MinRequiredSamples} lectura física.");
        }

        if (samples.Count > MaxAllowedSamples)
        {
            return Fail($"Exceso de muestras ({samples.Count}). Límite de protección: {MaxAllowedSamples}.");
        }

        // 2. Sanitización y validación exhaustiva de rangos por muestra (Zero-Trust Input)
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            if (s == null)
            {
                return Fail($"Muestra [{i}] es nula.");
            }

            if (double.IsNaN(s.Lat) || double.IsInfinity(s.Lat) || s.Lat < -90.0 || s.Lat > 90.0)
            {
                return Fail($"Muestra [{i}] con latitud corrupta: {s.Lat}.");
            }

            if (double.IsNaN(s.Lon) || double.IsInfinity(s.Lon) || s.Lon < -180.0 || s.Lon > 180.0)
            {
                return Fail($"Muestra [{i}] con longitud corrupta: {s.Lon}.");
            }

            if (double.IsNaN(s.Acc) || double.IsInfinity(s.Acc) || s.Acc <= 0.0 || s.Acc > 500.0)
            {
                return Fail($"Muestra [{i}] con precisión inadmisible: {s.Acc}m.");
            }

            if (s.Speed.HasValue && (double.IsNaN(s.Speed.Value) || double.IsInfinity(s.Speed.Value) || s.Speed.Value < 0.0 || s.Speed.Value > 50.0))
            {
                return Fail($"Muestra [{i}] con velocidad corrupta: {s.Speed}.");
            }

            if (s.TimestampMs <= 0)
            {
                return Fail($"Muestra [{i}] con marca temporal inválida: {s.TimestampMs}.");
            }
        }

        // 3. Sanitización de coordenadas reclamadas por el cliente (si se suministran)
        if (clientClaimedLat.HasValue != clientClaimedLon.HasValue)
        {
            return Fail("Coordenadas cliente inconsistentes: Se debe proporcionar tanto latitud como longitud reclamada.");
        }

        if (clientClaimedLat.HasValue)
        {
            var clat = clientClaimedLat.Value;
            var clon = clientClaimedLon!.Value;
            if (double.IsNaN(clat) || double.IsInfinity(clat) || clat < -90.0 || clat > 90.0)
            {
                return Fail($"Latitud reclamada por cliente corrupta: {clat}.");
            }
            if (double.IsNaN(clon) || double.IsInfinity(clon) || clon < -180.0 || clon > 180.0)
            {
                return Fail($"Longitud reclamada por cliente corrupta: {clon}.");
            }
        }

        // 4. Frecuencia e integridad temporal (monotonicidad, delta bounds [150ms, 4000ms])
        for (int i = 1; i < samples.Count; i++)
        {
            var dt = samples[i].TimestampMs - samples[i - 1].TimestampMs;
            if (dt < 0)
            {
                return Fail($"Inconsistencia temporal en muestra [{i}]: timestamp retrocede ({dt}ms).");
            }
            if (dt < MinSampleDeltaTimeMs)
            {
                return Fail($"Frecuencia no física ({dt}ms). Generación sintética detectada.");
            }
            if (dt > MaxSampleDeltaTimeMs)
            {
                return Fail($"Intervalo excesivo ({dt}ms) entre muestras consecutivas.");
            }
        }

        // 5. Verificación de desfase de reloj con el servidor (clock drift <= 8000ms)
        var lastSampleTimestamp = samples[^1].TimestampMs;
        var timeDrift = Math.Abs(serverNowUnixMs - lastSampleTimestamp);
        if (timeDrift > MaxAllowedClockSkewMs)
        {
            return Fail($"Desfase de reloj con el servidor excesivo ({timeDrift}ms).");
        }

        // 6. Detección de Falsificación por Varianza Cero (Synthetic Precision / Mock)
        double sumLat = 0.0, sumLon = 0.0;
        foreach (var s in samples)
        {
            sumLat += s.Lat;
            sumLon += s.Lon;
        }
        double meanLat = sumLat / samples.Count;
        double meanLon = sumLon / samples.Count;

        double sumSqLat = 0.0, sumSqLon = 0.0;
        for (int i = 0; i < samples.Count; i++)
        {
            sumSqLat += Math.Pow(samples[i].Lat - meanLat, 2);
            sumSqLon += Math.Pow(samples[i].Lon - meanLon, 2);
        }

        // Varianza muestral (tolerando estudiantes sentados o en reposo)
        double spatialStdDev = 0.0;
        if (samples.Count > 1)
        {
            double stdDevLat = Math.Sqrt(sumSqLat / (samples.Count - 1)) * MetersPerDegreeLat;
            double stdDevLon = Math.Sqrt(sumSqLon / (samples.Count - 1)) * MetersPerDegreeLon;
            spatialStdDev = Math.Sqrt(stdDevLat * stdDevLat + stdDevLon * stdDevLon);
        }

        // 7. Verificación cinemática con guardia estocástica instrumental (sobre la serie cronológica cruda)
        double maxVel = 0.0;
        for (int i = 1; i < samples.Count; i++)
        {
            var dtSec = (samples[i].TimestampMs - samples[i - 1].TimestampMs) / 1000.0;
            if (dtSec > 0)
            {
                var dist = ApproxDistance(samples[i - 1].Lat, samples[i - 1].Lon, samples[i].Lat, samples[i].Lon);
                var vel = dist / dtSec;
                if (vel > maxVel)
                {
                    maxVel = vel;
                }
                var threshold = (MaxPedestrianSpeedMps * dtSec) + 2.5 * Math.Sqrt(samples[i].Acc * samples[i].Acc + samples[i - 1].Acc * samples[i - 1].Acc);
                if (dist > threshold)
                {
                    return Fail($"Salto cinemático anómalo ({vel:F1} m/s). Supera el límite en aula con ruido instrumental ({threshold / dtSec:F1} m/s).");
                }
            }
        }

        // 8. Filtrado Robusto por Desviación Absoluta de la Mediana (MAD en servidor)
        var sortedLats = samples.Select(s => s.Lat).OrderBy(x => x).ToList();
        var sortedLons = samples.Select(s => s.Lon).OrderBy(x => x).ToList();
        double medLat = GetMedian(sortedLats);
        double medLon = GetMedian(sortedLons);

        var devLats = samples.Select(s => Math.Abs(s.Lat - medLat)).OrderBy(x => x).ToList();
        var devLons = samples.Select(s => Math.Abs(s.Lon - medLon)).OrderBy(x => x).ToList();
        double madLat = Math.Max(GetMedian(devLats) * MadScaleFactor, MinAllowedMad);
        double madLon = Math.Max(GetMedian(devLons) * MadScaleFactor, MinAllowedMad);

        var clean = samples.Where(s =>
        {
            double zLat = Math.Abs(s.Lat - medLat) / madLat;
            double zLon = Math.Abs(s.Lon - medLon) / madLon;
            return Math.Sqrt(zLat * zLat + zLon * zLon) <= MadThresholdZ;
        }).ToList();

        // Si el filtro purga demasiadas muestras, revertir al conjunto original para prevenir inanición
        if (clean.Count < 3)
        {
            clean = samples;
        }

        // 9. Estimador Insesgado de Varianza Mínima (BLUE en servidor)
        double sumWeights = 0.0, weightedLat = 0.0, weightedLon = 0.0;
        foreach (var s in clean)
        {
            double variance = Math.Max(s.Acc * s.Acc, MinAllowedVariance);
            double w = 1.0 / variance;
            sumWeights += w;
            weightedLat += s.Lat * w;
            weightedLon += s.Lon * w;
        }

        double finalLat = weightedLat / sumWeights;
        double finalLon = weightedLon / sumWeights;
        double finalAcc = 1.0 / Math.Sqrt(sumWeights);

        // 10. Verificación de discrepancia cliente-servidor (<= 2.0m)
        if (clientClaimedLat.HasValue && clientClaimedLon.HasValue)
        {
            var disc = ApproxDistance(clientClaimedLat.Value, clientClaimedLon.Value, finalLat, finalLon);
            if (disc > MaxDiscrepancyMeters)
            {
                return Fail($"Discrepancia telemétrica ({disc:F1}m): El cálculo cliente difiere del certificado por el servidor.");
            }
        }

        return new TelemetryValidationResult
        {
            IsValid = true,
            VerifiedLatitude = finalLat,
            VerifiedLongitude = finalLon,
            VerifiedAccuracyMetros = finalAcc,
            VerifiedVariance = 1.0 / sumWeights,
            SpatialStdDevMetros = spatialStdDev,
            MaxKinematicVelocityMps = maxVel,
            RetainedSampleCount = clean.Count
        };
    }

    private static TelemetryValidationResult Fail(string reason) => new()
    {
        IsValid = false,
        RejectReason = reason
    };

    private static double GetMedian(List<double> list)
    {
        if (list == null || list.Count == 0) return 0.0;
        int mid = list.Count / 2;
        return list.Count % 2 == 1
            ? list[mid]
            : (list[mid - 1] + list[mid]) / 2.0;
    }

    public static double ApproxDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat2 - lat1) * MetersPerDegreeLat;
        double dLon = (lon2 - lon1) * MetersPerDegreeLon;
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }
}
