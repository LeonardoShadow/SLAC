using System;
using System.Collections.Generic;
using System.Text.Json;
using SLAC.Core.Attendance.Services;
using SLAC.Features.Attendance.Hubs;
using Xunit;

namespace SLAC.Tests;

public class InertialFusionZuptTests
{
    [Fact]
    public void RenderRegistrationView_ContainsZuptInertialFusionAndThreshold()
    {
        var sesionId = Guid.NewGuid();
        var html = AttendanceStudentView.RenderRegistrationView(
            sesionId,
            "qr_token_test_abc",
            "Física I",
            "FIS-101",
            modo: "gps",
            radioTolerancia: 25,
            latReferencia: -17.7833,
            lonReferencia: -63.1821);

        Assert.NotNull(html);
        Assert.Contains("devicemotion", html);
        Assert.Contains("STATIONARY_VARIANCE_THRESHOLD = 0.25", html);
        Assert.Contains("applyZuptFilter", html);
        Assert.Contains("name=\"isStationary\"", html);
        Assert.Contains("name=\"accelerationVariance\"", html);
        Assert.Contains("DeviceMotionEvent", html);
    }

    [Fact]
    public void RenderGpsAutoVerifyView_ContainsZuptInertialFusionAndUIIndicator()
    {
        var sesionId = Guid.NewGuid();
        var html = AttendanceStudentView.RenderGpsAutoVerifyView(
            sesionId,
            "qr_token_test_abc",
            "Cálculo II",
            "MAT-102",
            radioTolerancia: 25,
            latReferencia: -17.7833,
            lonReferencia: -63.1821,
            modo: "gps_alpha");

        Assert.NotNull(html);
        Assert.Contains("devicemotion", html);
        Assert.Contains("telInertial", html);
        Assert.Contains("STATIONARY_VARIANCE_THRESHOLD = 0.25", html);
        Assert.Contains("applyZuptFilter", html);
        Assert.Contains("name=\"isStationary\"", html);
        Assert.Contains("name=\"accelerationVariance\"", html);
        Assert.Contains("DeviceMotionEvent", html);
    }

    [Fact]
    public void RawGpsTelemetrySample_SupportsIsStationaryAndAccelerationVariance()
    {
        var sample = new RawGpsTelemetrySample
        {
            Lat = -17.783321,
            Lon = -63.182145,
            Acc = 4.2,
            Speed = 0.0,
            TimestampMs = 1730000000000,
            IsStationary = true,
            AccelerationVariance = 0.0412
        };

        var json = JsonSerializer.Serialize(sample);
        Assert.Contains("\"IsStationary\":true", json);
        Assert.Contains("\"AccelerationVariance\":0.0412", json);

        var deserialized = JsonSerializer.Deserialize<RawGpsTelemetrySample>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsStationary);
        Assert.Equal(0.0412, deserialized.AccelerationVariance);
    }

    [Fact]
    public void AttendanceScanRequest_SupportsIsStationaryAndAccelerationVariance()
    {
        var request = new AttendanceScanRequest
        {
            SesionId = Guid.NewGuid(),
            QrToken = "qr_token_xyz",
            IsStationary = true,
            AccelerationVariance = 0.0385,
            Samples =
            [
                new RawGpsTelemetrySample
                {
                    Lat = -17.783321,
                    Lon = -63.182145,
                    Acc = 4.0,
                    Speed = 0.0,
                    TimestampMs = 1730000000000,
                    IsStationary = true,
                    AccelerationVariance = 0.0385
                }
            ]
        };

        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"IsStationary\":true", json);
        Assert.Contains("\"AccelerationVariance\":0.0385", json);

        var deserialized = JsonSerializer.Deserialize<AttendanceScanRequest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsStationary);
        Assert.Equal(0.0385, deserialized.AccelerationVariance);
    }

    [Fact]
    public void ZuptAlgorithm_MathematicalVerification_StaticDesk()
    {
        // Simulación matemática del algoritmo de varianza triaxial:
        // Dispositivo en reposo con ruido sensoril gaussiano de baja amplitud (~0.04 m/s^2)
        var rand = new Random(42);
        var samplesX = new List<double>();
        var samplesY = new List<double>();
        var samplesZ = new List<double>();

        for (int i = 0; i < 60; i++)
        {
            samplesX.Add((rand.NextDouble() - 0.5) * 0.08); // ax centrado en 0
            samplesY.Add((rand.NextDouble() - 0.5) * 0.08); // ay centrado en 0
            samplesZ.Add(9.81 + (rand.NextDouble() - 0.5) * 0.08); // az gravedad constante 9.81
        }

        // Varianzas por eje
        double varX = ComputeVariance(samplesX);
        double varY = ComputeVariance(samplesY);
        double varZ = ComputeVariance(samplesZ);

        // Varianza triaxial combinada: sigma(a) = sqrt(Var(ax) + Var(ay) + Var(az))
        double sigmaA = Math.Sqrt(varX + varY + varZ);

        Assert.True(sigmaA < 0.25, $"La varianza triaxial {sigmaA} debe ser menor al umbral 0.25 m/s² en reposo");
    }

    [Fact]
    public void ZuptAlgorithm_MathematicalVerification_WalkingMotion()
    {
        // Simulación matemática de estudiante caminando con aceleraciones sinusoidales
        var samplesX = new List<double>();
        var samplesY = new List<double>();
        var samplesZ = new List<double>();

        for (int i = 0; i < 60; i++)
        {
            double t = i * 0.02;
            samplesX.Add(1.2 * Math.Sin(2.0 * Math.PI * 1.5 * t));
            samplesY.Add(0.8 * Math.Cos(2.0 * Math.PI * 1.5 * t));
            samplesZ.Add(9.81 + 2.5 * Math.Sin(2.0 * Math.PI * 3.0 * t));
        }

        double varX = ComputeVariance(samplesX);
        double varY = ComputeVariance(samplesY);
        double varZ = ComputeVariance(samplesZ);
        double sigmaA = Math.Sqrt(varX + varY + varZ);

        Assert.True(sigmaA >= 0.25, $"La varianza triaxial {sigmaA} debe ser >= 0.25 m/s² en movimiento");
    }

    private static double ComputeVariance(List<double> values)
    {
        int n = values.Count;
        if (n == 0) return 0.0;
        double mean = 0.0;
        for (int i = 0; i < n; i++) mean += values[i];
        mean /= n;

        double sumSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d = values[i] - mean;
            sumSq += d * d;
        }
        return sumSq / n;
    }
}
