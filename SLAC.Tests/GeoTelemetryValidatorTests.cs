namespace SLAC.Tests;

using System;
using System.Collections.Generic;
using SLAC.Core.Attendance.Services;
using Xunit;

public class GeoTelemetryValidatorTests
{
    private const double BaseLat = -17.839210;
    private const double BaseLon = -63.194244;
    private readonly long _baseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private List<RawGpsTelemetrySample> CreateValidSamples(int count = 10, double noiseMeters = 1.0)
    {
        var list = new List<RawGpsTelemetrySample>();
        var rand = new Random(42); // Semilla fija para reproducibilidad determinista
        for (int i = 0; i < count; i++)
        {
            // Variación estocástica controlada superior a 0.05m
            double dLatMeters = (rand.NextDouble() - 0.5) * 2.0 * noiseMeters;
            double dLonMeters = (rand.NextDouble() - 0.5) * 2.0 * noiseMeters;

            list.Add(new RawGpsTelemetrySample
            {
                Lat = BaseLat + (dLatMeters / GeoTelemetryValidator.MetersPerDegreeLat),
                Lon = BaseLon + (dLonMeters / GeoTelemetryValidator.MetersPerDegreeLon),
                Acc = 6.0 + (rand.NextDouble() * 2.0),
                Speed = 0.5,
                TimestampMs = _baseTime - ((count - 1 - i) * 1000)
            });
        }
        return list;
    }

    [Fact]
    public void Validate_NullSamples_ReturnsFail()
    {
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(null, _baseTime);
        Assert.False(result.IsValid);
        Assert.Contains("Muestras insuficientes", result.RejectReason);
    }

    [Fact]
    public void Validate_ZeroSamples_ReturnsFail()
    {
        var samples = CreateValidSamples(0);
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(result.IsValid);
        Assert.Contains("Muestras insuficientes", result.RejectReason);
    }

    [Fact]
    public void Validate_SingleSample_SucceedsForRealMobile()
    {
        var samples = CreateValidSamples(1);
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_GreaterThan50Samples_ReturnsFail()
    {
        var samples = CreateValidSamples(55);
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(result.IsValid);
        Assert.Contains("Exceso de muestras (55)", result.RejectReason);
    }

    [Fact]
    public void Validate_NullElementInSamples_ReturnsFail()
    {
        var samples = CreateValidSamples(5);
        samples[2] = null!;
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(result.IsValid);
        Assert.Contains("Muestra [2] es nula", result.RejectReason);
    }

    [Fact]
    public void Validate_CorruptCoordinateValues_ReturnsFail()
    {
        var samples = CreateValidSamples(5);
        samples[1].Lat = double.NaN;
        var res1 = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(res1.IsValid);
        Assert.Contains("latitud corrupta", res1.RejectReason);

        samples = CreateValidSamples(5);
        samples[3].Acc = -1.0;
        var res2 = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(res2.IsValid);
        Assert.Contains("precisión inadmisible", res2.RejectReason);

        samples = CreateValidSamples(5);
        samples[0].Acc = 0.0; // Precisión cero es físicamente imposible
        var res3 = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(res3.IsValid);
        Assert.Contains("precisión inadmisible", res3.RejectReason);
    }

    [Fact]
    public void Validate_TemporalInversion_NegativeDelta_ReturnsFail()
    {
        var samples = CreateValidSamples(5);
        samples[3].TimestampMs = samples[2].TimestampMs - 500;
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(result.IsValid);
        Assert.Contains("Inconsistencia temporal", result.RejectReason);
    }

    [Fact]
    public void Validate_ExcessiveSamplingRate_Under150ms_ToleratedForFastMobileBurst()
    {
        var samples = CreateValidSamples(5);
        samples[2].TimestampMs = samples[1].TimestampMs + 80;
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ExcessiveTemporalGap_Over120s_ReturnsFail()
    {
        var samples = CreateValidSamples(5);
        samples[3].TimestampMs = samples[2].TimestampMs + 150000; // 150s > 120s
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(result.IsValid);
        Assert.Contains("Intervalo excesivo", result.RejectReason);
    }

    [Fact]
    public void Validate_IndoorNormalGap_5540ms_Succeeds()
    {
        var samples = CreateValidSamples(5);
        samples[3].TimestampMs = samples[2].TimestampMs + 5540; // 5.54s en aula
        samples[4].TimestampMs = samples[3].TimestampMs + 1000;
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, samples[4].TimestampMs);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ClockSkewExceedsLimit_ReturnsFail()
    {
        var samples = CreateValidSamples(5);
        long serverFarAhead = _baseTime + 400000; // 400s desfase > 300s
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, serverFarAhead);
        Assert.False(result.IsValid);
        Assert.Contains("Desfase de reloj con el servidor excesivo", result.RejectReason);
    }

    [Fact]
    public void Validate_IdenticalCoordinatesAcrossAllSamples_SucceedsForStationaryStudent()
    {
        var samples = CreateValidSamples(7);
        for (int i = 0; i < samples.Count; i++)
        {
            samples[i].Lat = BaseLat;
            samples[i].Lon = BaseLon;
        }

        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_SubCentimeterSyntheticJitter_SucceedsForRealMobile()
    {
        var samples = CreateValidSamples(6);
        for (int i = 0; i < samples.Count; i++)
        {
            samples[i].Lat = BaseLat + (i * 0.001 / GeoTelemetryValidator.MetersPerDegreeLat);
            samples[i].Lon = BaseLon;
        }

        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_GrossOutlier_FilteredByMAD_RetainedCountReflectsPurge()
    {
        var samples = CreateValidSamples(10, noiseMeters: 0.8);
        // Inyectar un valor atípico de multi-trayecto (multipath) de 15 metros en la muestra 6
        samples[6].Lat += (15.0 / GeoTelemetryValidator.MetersPerDegreeLat);

        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.True(result.IsValid, result.RejectReason);
        Assert.Equal(9, result.RetainedSampleCount); // El valor atípico fue purgado limpiamente por MAD

        // La coordenada certificada final no debe haber sido arrastrada por el outlier de 15m
        double distFromBase = GeoTelemetryValidator.ApproxDistance(BaseLat, BaseLon, result.VerifiedLatitude, result.VerifiedLongitude);
        Assert.True(distFromBase < 5.0, $"La distancia al centroide no debe ser alterada por el outlier ({distFromBase}m)");
    }

    [Fact]
    public void Validate_KinematicStarvationPrevention_HighNoiseWithinStochasticGuard_Succeeds()
    {
        var samples = CreateValidSamples(8);
        // Simular salto por ruido de 18 metros con precisión alta (10m) en 1 segundo
        samples[4].Lat = samples[3].Lat + (18.0 / GeoTelemetryValidator.MetersPerDegreeLat);
        samples[4].Acc = 10.0;
        samples[3].Acc = 10.0;
        // Umbral estocástico: 3.5*1.0 + 2.5*sqrt(100+100) = 3.5 + 35.35 = 38.85m > 18.0m -> DEBE ACEPTARSE

        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.True(result.IsValid, result.RejectReason);
    }

    [Fact]
    public void Validate_SevereKinematicTeleport_ExceedsStochasticThreshold_ReturnsFail()
    {
        var samples = CreateValidSamples(6);
        // Teletransporte de 100 metros en 1 segundo con precisión instrumental de 3 metros
        samples[4].Lat = samples[3].Lat + (100.0 / GeoTelemetryValidator.MetersPerDegreeLat);
        samples[4].Acc = 3.0;
        samples[3].Acc = 3.0;
        // Umbral estocástico: 3.5*1.0 + 2.5*sqrt(9+9) = 3.5 + 10.6 = 14.1m << 100m -> RECHAZADO

        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);
        Assert.False(result.IsValid);
        Assert.Contains("Salto cinemático anómalo", result.RejectReason);
    }

    [Fact]
    public void Validate_ClientDiscrepancyCheck_Exceeds2Meters_ReturnsFail()
    {
        var samples = CreateValidSamples(8, noiseMeters: 0.5);

        // Cliente reclama estar 3.5 metros desplazado respecto al recálculo en servidor
        double claimedLat = BaseLat + (80.0 / GeoTelemetryValidator.MetersPerDegreeLat);
        double claimedLon = BaseLon;

        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime, claimedLat, claimedLon);
        Assert.False(result.IsValid);
        Assert.Contains("Discrepancia telemétrica", result.RejectReason);
    }

    [Fact]
    public void Validate_ValidRealisticSamples_SucceedsWithAccurateBlueCertification()
    {
        var samples = CreateValidSamples(10, noiseMeters: 1.2);
        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, _baseTime);

        Assert.True(result.IsValid, result.RejectReason);
        Assert.True(result.VerifiedLatitude < -17.0 && result.VerifiedLatitude > -18.0);
        Assert.True(result.VerifiedLongitude < -63.0 && result.VerifiedLongitude > -64.0);
        Assert.True(result.VerifiedAccuracyMetros > 0.0 && result.VerifiedAccuracyMetros < 10.0);
        Assert.True(result.SpatialStdDevMetros >= GeoTelemetryValidator.MinSpatialStdDevMeters);
        Assert.True(result.RetainedSampleCount >= 5);
        Assert.Equal(result.VerifiedLatitude, result.CertifiedLat);
    }
}
