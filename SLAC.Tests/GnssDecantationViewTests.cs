namespace SLAC.Tests;

using System;
using Xunit;
using SLAC.Features.Attendance.Hubs;

public class GnssDecantationViewTests
{
    [Fact]
    public void RenderRegistrationView_IncludesAsymptoticSettlingAndDecantationComponents()
    {
        // Arrange
        var sesionId = Guid.NewGuid();
        var token = "mock_token_reg";

        // Act
        var html = AttendanceStudentView.RenderRegistrationView(sesionId, token, "Física II", "FIS-201");

        // Assert - Ventana temporal y meseta de convergencia (2.0 a 2.8s, timeout 3.0s)
        Assert.Contains("MIN_DECANT_MS = 2000", html);
        Assert.Contains("TARGET_DECANT_MS = 2800", html);
        Assert.Contains("HARD_TIMEOUT_MS = 3000", html);

        // Assert - Elementos de radar e interfaces UI
        Assert.Contains("autoGpsBadge", html);
        Assert.Contains("gps-status", html);
        Assert.Contains("samplesJson", html);

        // Assert - Algoritmo de decantación y watchPosition
        Assert.Contains("finalizarDecantacion", html);
        Assert.Contains("watchPosition", html);
        Assert.Contains("formatearProgreso", html);
        Assert.Contains("Calibrando satélites", html);
        Assert.Contains("¡Fijado!", html);
    }

    [Fact]
    public void RenderGpsAutoVerifyView_IncludesAsymptoticSettlingAndDecantationComponents()
    {
        // Arrange
        var sesionId = Guid.NewGuid();
        var token = "mock_token_autoverify";

        // Act
        var html = AttendanceStudentView.RenderGpsAutoVerifyView(
            sesionId,
            token,
            "Cálculo I",
            "MAT-101",
            radioTolerancia: 25,
            latReferencia: -17.776200,
            lonReferencia: -63.182100);

        // Assert - Radar badge y telemetría en vivo
        Assert.Contains("statusBadge", html);
        Assert.Contains("telemetryBox", html);
        Assert.Contains("samplesJson", html);

        // Assert - Ventana temporal y meseta de convergencia (2.0 a 2.8s, timeout 3.0s)
        Assert.Contains("MIN_DECANT_MS = 2000", html);
        Assert.Contains("TARGET_DECANT_MS = 2800", html);
        Assert.Contains("HARD_TIMEOUT_MS = 3000", html);

        // Assert - Algoritmo de decantación y watchPosition
        Assert.Contains("finalizarDecantacion", html);
        Assert.Contains("watchPosition", html);
        Assert.Contains("formatearProgreso", html);
        Assert.Contains("Calibrando satélites", html);
        Assert.Contains("¡Fijado!", html);
    }
}
