namespace SLAC.Tests;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Security;
using Xunit;

/// <summary>
/// Pruebas unitarias para la Capa 3:
/// D-GPS Relativo en Servidor, ZUPT Inercial y Techo Estricto de 35 metros para Geocercas Adaptativas.
/// </summary>
public class RelativeDgpsAndZuptTests
{
    private readonly VincentyGeodesicService _geodesicService;

    // Coordenadas oficiales de calibración UPDS Santa Cruz (Campus Sur)
    private const double P0_Lat = -17.839209876871866;
    private const double P0_Lon = -63.194244105580020;

    private const double P1_Lat = -17.839230579876050;
    private const double P1_Lon = -63.194158285959020;

    private const double P2_Lat = -17.839256987898356;
    private const double P2_Lon = -63.194059021282996;

    public RelativeDgpsAndZuptTests()
    {
        var config = Options.Create(new CampusGeofenceConfig());
        _geodesicService = new VincentyGeodesicService(config, NullLogger<VincentyGeodesicService>.Instance);
    }

    [Fact]
    public void DgpsRelativo_CommonAtmosphericShift_IsCancelledCompletely()
    {
        // Ground truth: Docente en P0, Estudiante en P1 (Distancia teórica: 9.381m)
        double theoreticalDist = _geodesicService.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P1_Lat, P1_Lon);
        Assert.Equal(9.381070, theoreticalDist, precision: 4);

        // Simular distorsión iono-troposférica común severa (+25m Norte, +18m Este) en el aula
        double deltaLat = 25.0 / GeoTelemetryValidator.MetersPerDegreeLat;
        double deltaLon = 18.0 / GeoTelemetryValidator.MetersPerDegreeLon;

        double teacherMeasuredLat = P0_Lat + deltaLat;
        double teacherMeasuredLon = P0_Lon + deltaLon;
        double studentMeasuredLat = P1_Lat + deltaLat;
        double studentMeasuredLon = P1_Lon + deltaLon;

        var now = DateTimeOffset.UtcNow;

        // 1. Ejecutar compensación diferencial relativa mediante VincentyGeodesicService
        bool success = _geodesicService.AplicarCorreccionDiferencialRelativa(
            teacherMeasuredLat, teacherMeasuredLon,
            P0_Lat, P0_Lon,
            studentMeasuredLat, studentMeasuredLon,
            now.AddSeconds(-30), now,
            out var correctedStudentLat, out var correctedStudentLon,
            out var sesgoAtm);

        Assert.True(success);
        Assert.InRange(sesgoAtm, 30.5, 31.0); // sqrt(25^2 + 18^2) = 30.8m

        // El punto corregido debe coincidir exactamente con P1 (Ground Truth)
        double residualErrorMeters = _geodesicService.CalcularDistanciaVincentyWgs84(P1_Lat, P1_Lon, correctedStudentLat, correctedStudentLon);
        Assert.True(residualErrorMeters < 0.005, $"El error residual tras D-GPS ({residualErrorMeters:F4}m) debe ser subcentimétrico.");

        // 2. Ejecutar también mediante el validador estático GeoTelemetryValidator
        var dgpsResult = GeoTelemetryValidator.ApplyRelativeDifferentialCorrection(
            teacherMeasuredLat, teacherMeasuredLon,
            P0_Lat, P0_Lon,
            studentMeasuredLat, studentMeasuredLon,
            now.AddSeconds(-30).ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds());

        Assert.True(dgpsResult.IsApplied);
        Assert.Equal(9.381, TheoreticalMeters(dgpsResult.RelativeDistanceMeters), tolerance: 0.01);
    }

    [Fact]
    public void DgpsRelativo_TemporalWindowExpired_FallsBackSafely()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredTeacherTime = now.AddMinutes(-35); // 35 minutos > ventana de 20 minutos

        bool success = _geodesicService.AplicarCorreccionDiferencialRelativa(
            P0_Lat + 0.0001, P0_Lon + 0.0001,
            P0_Lat, P0_Lon,
            P1_Lat + 0.0001, P1_Lon + 0.0001,
            expiredTeacherTime, now,
            out var correctedLat, out var correctedLon,
            out var sesgoAtm);

        Assert.False(success);
        Assert.Equal(P1_Lat + 0.0001, correctedLat);
        Assert.Equal(P1_Lon + 0.0001, correctedLon);
        Assert.Equal(0.0, sesgoAtm);
    }

    [Fact]
    public void DgpsRelativo_AnomalousAtmosphericShiftExceeds150m_RejectedAsOutlier()
    {
        var now = DateTimeOffset.UtcNow;

        // Desfase artificial masivo de 300 metros
        double shiftLat = 300.0 / GeoTelemetryValidator.MetersPerDegreeLat;

        bool success = _geodesicService.AplicarCorreccionDiferencialRelativa(
            P0_Lat + shiftLat, P0_Lon,
            P0_Lat, P0_Lon,
            P1_Lat + shiftLat, P1_Lon,
            now, now,
            out var correctedLat, out var correctedLon,
            out _);

        Assert.False(success, "Un sesgo mayor a 150m debe rechazarse para evitar spoofing o coordenadas de aula desactualizadas.");
        Assert.Equal(P1_Lat + shiftLat, correctedLat);
    }

    [Fact]
    public void ZuptInertial_StationaryDevice_ResidualKinematicJump_BlueWeightingMaintainsUnbiasedCentroid()
    {
        long baseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samplesStationary = new List<RawGpsTelemetrySample>();
        var samplesStandard = new List<RawGpsTelemetrySample>();

        // Desplazamientos simétricos en metros alrededor de Ground Truth P1 con un salto residual retenido por MAD (Z = 2.88 <= 3.0)
        double[] offsetsMeters = [-1.5, -1.0, -0.5, 0.0, 0.0, 0.0, 0.5, 1.0, 1.5, 3.2];

        for (int i = 0; i < offsetsMeters.Length; i++)
        {
            double dLat = offsetsMeters[i] / GeoTelemetryValidator.MetersPerDegreeLat;

            var s1 = new RawGpsTelemetrySample
            {
                Lat = P1_Lat + dLat,
                Lon = P1_Lon,
                Acc = 4.0,
                Speed = 0.0,
                TimestampMs = baseTime + (i * 1000),
                IsStationary = true,
                AccelerationVariance = 0.015 // Acelerómetro en reposo
            };

            var s2 = new RawGpsTelemetrySample
            {
                Lat = s1.Lat,
                Lon = s1.Lon,
                Acc = s1.Acc,
                Speed = 0.5,
                TimestampMs = s1.TimestampMs,
                IsStationary = false
            };

            samplesStationary.Add(s1);
            samplesStandard.Add(s2);
        }

        // Validar con ZUPT estacionario
        var resultZupt = GeoTelemetryValidator.ValidateAndDeriveLocation(samplesStationary, baseTime + 11000, isStationary: true);
        Assert.True(resultZupt.IsValid);
        Assert.True(resultZupt.IsStationaryCertified);
        Assert.NotNull(resultZupt.InertialAccelerationVariance);

        // Validar sin ZUPT estacionario
        var resultStd = GeoTelemetryValidator.ValidateAndDeriveLocation(samplesStandard, baseTime + 11000, isStationary: false);
        Assert.True(resultStd.IsValid);
        Assert.False(resultStd.IsStationaryCertified);

        // La ponderación adaptativa ZUPT BLUE penaliza severamente el salto residual de 2.0m, por lo que el centroide ZUPT
        // es significativamente más insesgado (más cercano a P1) que el estándar
        double distZupt = GeoTelemetryValidator.ApproxDistance(P1_Lat, P1_Lon, resultZupt.VerifiedLatitude, resultZupt.VerifiedLongitude);
        double distStd = GeoTelemetryValidator.ApproxDistance(P1_Lat, P1_Lon, resultStd.VerifiedLatitude, resultStd.VerifiedLongitude);

        Assert.True(distZupt < distStd, $"El centroide ZUPT ({distZupt:F3}m) debe ser estrictamente más insesgado que el estándar ({distStd:F3}m).");
    }

    [Fact]
    public void ZuptInertial_SuddenKinematicJump_ViolatesStationaryBudget_Rejected()
    {
        long baseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = new List<RawGpsTelemetrySample>();

        for (int i = 0; i < 5; i++)
        {
            samples.Add(new RawGpsTelemetrySample
            {
                Lat = P1_Lat,
                Lon = P1_Lon,
                Acc = 2.0,
                Speed = 0.0,
                TimestampMs = baseTime + (i * 1000),
                IsStationary = true,
                AccelerationVariance = 0.02
            });
        }

        // Teletransporte cinemático de 8 metros en 1 segundo con precisión instrumental alta (2m)
        // En reposo estacionario ZUPT (budget = 1.5 m/s): threshold = (1.5 * 1) + 2.0 * sqrt(4+4) = 1.5 + 5.65 = 7.15m < 8.0m -> DEBE RECHAZARSE
        samples[4].Lat += 8.0 / GeoTelemetryValidator.MetersPerDegreeLat;

        var result = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, baseTime + 5000, isStationary: true);
        Assert.False(result.IsValid);
        Assert.Contains("reposo estacionario ZUPT", result.RejectReason);
    }

    [Fact]
    public void ZuptInertial_CorruptAccelerationVariance_RejectedByZeroTrust()
    {
        long baseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = new List<RawGpsTelemetrySample>
        {
            new() { Lat = P1_Lat, Lon = P1_Lon, Acc = 5.0, TimestampMs = baseTime }
        };

        var resNeg = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, baseTime, accelerationVariance: -0.5);
        Assert.False(resNeg.IsValid);
        Assert.Contains("Varianza de aceleración inercial corrupta", resNeg.RejectReason);

        var resNan = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, baseTime, accelerationVariance: double.NaN);
        Assert.False(resNan.IsValid);
        Assert.Contains("Varianza de aceleración inercial corrupta", resNan.RejectReason);
    }

    [Fact]
    public void GeocercaAdaptativa_Strict35MeterCeiling_EnforcedAcrossAllModes()
    {
        // 1. Con aula grande (nominal = 40m) o ruido extremo, no debe superar 35.0m
        double radioConTecho40 = _geodesicService.CalcularRadioGeocercaAdaptativaConTecho(
            sigmaDocente: 20.0,
            sigmaEstudiante: 50.0,
            radioNominalAula: 40,
            techoMaximo: 35.0);

        Assert.Equal(35.0, radioConTecho40);

        // 2. Con aula estándar (nominal = 12m), calcula el radio adaptativo normalmente (28.0m bajo ruido extremo)
        double radioConTecho12 = _geodesicService.CalcularRadioGeocercaAdaptativaConTecho(
            sigmaDocente: 20.0,
            sigmaEstudiante: 50.0,
            radioNominalAula: 12,
            techoMaximo: 35.0);

        Assert.Equal(28.0, radioConTecho12);

        // 3. Con aula de 30m y ruido moderado (30 + 2.5 + 5.0 = 37.5m), debe topar exactamente en 35.0m
        double radioAuditorio30 = _geodesicService.CalcularRadioGeocercaAdaptativaConTecho(
            sigmaDocente: 5.0,
            sigmaEstudiante: 5.0,
            radioNominalAula: 30,
            techoMaximo: 35.0);

        Assert.Equal(35.0, radioAuditorio30);
    }

    private static double TheoreticalMeters(double value) => Math.Round(value, 3);
}
