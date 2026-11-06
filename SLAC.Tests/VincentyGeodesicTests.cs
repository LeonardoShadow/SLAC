namespace SLAC.Tests;

using System;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SLAC.Core.Security;
using Xunit;

public class VincentyGeodesicTests
{
    private readonly VincentyGeodesicService _service;

    // Coordenadas oficiales de calibración UPDS (PLAN_MAESTRO_GPS_ALPHA.md Sec 4.1)
    private const double P0_Lat = -17.839209876871866;
    private const double P0_Lon = -63.194244105580020;

    private const double P1_Lat = -17.839230579876050;
    private const double P1_Lon = -63.194158285959020;

    private const double P2_Lat = -17.839256987898356;
    private const double P2_Lon = -63.194059021282996;

    public VincentyGeodesicTests()
    {
        var config = Options.Create(new CampusGeofenceConfig());
        _service = new VincentyGeodesicService(config, NullLogger<VincentyGeodesicService>.Instance);
    }

    [Fact]
    public void CalcularDistanciaVincentyWgs84_GroundTruthP0P1_ReturnsExact9_381Meters()
    {
        var dist = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P1_Lat, P1_Lon);
        Assert.InRange(dist, 9.376, 9.386);
        Assert.Equal(9.381070, dist, precision: 4);
    }

    [Fact]
    public void CalcularDistanciaVincentyWgs84_GroundTruthP0P2_ReturnsExact20_300Meters()
    {
        var dist = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P2_Lat, P2_Lon);
        Assert.InRange(dist, 20.295, 20.305);
        Assert.Equal(20.300103, dist, precision: 4);
    }

    [Fact]
    public void CalcularDistanciaVincentyWgs84_GroundTruthP1P2_ReturnsExact10_921Meters()
    {
        var dist = _service.CalcularDistanciaVincentyWgs84(P1_Lat, P1_Lon, P2_Lat, P2_Lon);
        Assert.InRange(dist, 10.915, 10.925);
        Assert.Equal(10.920509, dist, precision: 4);
    }

    [Fact]
    public void CalcularDistanciaVincentyWgs84_CollinearityDefect_Matches1_48Millimeters()
    {
        var d01 = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P1_Lat, P1_Lon);
        var d12 = _service.CalcularDistanciaVincentyWgs84(P1_Lat, P1_Lon, P2_Lat, P2_Lon);
        var d02 = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P2_Lat, P2_Lon);

        var colinearDefect = Math.Abs((d01 + d12) - d02);
        // Defecto teórico certificado: 0.001477 metros (1.48 mm)
        Assert.InRange(colinearDefect, 0.0010, 0.0020);
    }

    [Fact]
    public void CalcularDistanciaVincentyWgs84_IdenticalCoordinates_ReturnsZero()
    {
        var dist = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P0_Lat, P0_Lon);
        Assert.Equal(0.0, dist);
    }

    [Fact]
    public void CalcularDistanciaVincentyWgs84_SymmetricProperty_DistanceIsEqualBothWays()
    {
        var distForward = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P1_Lat, P1_Lon);
        var distReverse = _service.CalcularDistanciaVincentyWgs84(P1_Lat, P1_Lon, P0_Lat, P0_Lon);
        Assert.Equal(distForward, distReverse, precision: 9);
    }

    [Fact]
    public void CalcularDistanciaVincentyWgs84_EquatorialPoints_DoesNotDivideByZero()
    {
        // Dos puntos sobre el ecuador separados 1 grado de longitud
        var dist = _service.CalcularDistanciaVincentyWgs84(0.0, 0.0, 0.0, 1.0);
        // 1 grado en el ecuador elipsoidal es aprox 111,319.49 metros
        Assert.InRange(dist, 111300.0, 111350.0);
    }

    [Fact]
    public void CalcularDistanciaVincentyWgs84_AntipodalPoints_GracefulFallbackWithoutInfiniteLoop()
    {
        // Puntos casi antípodas (la fórmula de Vincenty estándar diverge y debe conmutar a Haversine)
        var dist = _service.CalcularDistanciaVincentyWgs84(0.0, 0.0, 0.5, 179.8);
        Assert.True(dist > 19000000.0);
    }

    [Theory]
    [InlineData(double.NaN, 0.0, 0.0, 0.0)]
    [InlineData(0.0, double.PositiveInfinity, 0.0, 0.0)]
    [InlineData(-95.0, 0.0, 0.0, 0.0)]
    [InlineData(0.0, 185.0, 0.0, 0.0)]
    public void CalcularDistanciaVincentyWgs84_InvalidCoordinates_ThrowsException(double lat1, double lon1, double lat2, double lon2)
    {
        Assert.ThrowsAny<ArgumentException>(() => _service.CalcularDistanciaVincentyWgs84(lat1, lon1, lat2, lon2));
    }

    [Fact]
    public void CalcularDistanciaHaversineGauss_GroundTruthCoordinates_MatchesCertifiedTable()
    {
        var d01 = _service.CalcularDistanciaHaversineGauss(P0_Lat, P0_Lon, P1_Lat, P1_Lon);
        var d02 = _service.CalcularDistanciaHaversineGauss(P0_Lat, P0_Lon, P2_Lat, P2_Lon);

        Assert.Equal(9.355973, d01, precision: 4);
        Assert.Equal(20.246577, d02, precision: 4);
    }

    [Fact]
    public void CalcularRadioGeocercaAdaptativa_Nominal12Meters_CalculatesCorrectRadius()
    {
        var rMin = _service.CalcularRadioGeocercaAdaptativa(2.0, 2.0, 12);
        Assert.Equal(15.0, rMin); // 12 + 0.5*2 + 1.0*2 = 15.0

        var rMed = _service.CalcularRadioGeocercaAdaptativa(5.0, 5.0, 12);
        Assert.Equal(19.5, rMed); // 12 + 0.5*5 + 1.0*5 = 19.5
    }

    [Fact]
    public void CalcularRadioGeocercaAdaptativa_HighNoise_Enforces28MeterCeiling()
    {
        // Ruido extremo docente (20m) y estudiante (50m) acotados a 8m y 12m
        var rMax = _service.CalcularRadioGeocercaAdaptativa(20.0, 50.0, 12);
        Assert.Equal(28.0, rMax); // 12 + 0.5*8 + 1.0*12 = 28.0
    }

    [Fact]
    public void CalcularRadioGeocercaAdaptativa_Auditorium22Meters_Enforces28MeterCeiling()
    {
        // En auditorio, 22 + 2.5 + 5.0 = 29.5m, debe topar en 28.0m
        var rAuditorio = _service.CalcularRadioGeocercaAdaptativa(5.0, 5.0, 22);
        Assert.Equal(28.0, rAuditorio);
    }

    [Fact]
    public void GeofenceDiscrimination_15MeterGeofence_P1Inside_P2Outside()
    {
        var d01 = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P1_Lat, P1_Lon);
        var d02 = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P2_Lat, P2_Lon);

        double geocercaRadio = 15.0;
        Assert.True(d01 <= geocercaRadio, "P1 (9.38m) debe estar dentro de la geocerca de 15m.");
        Assert.False(d02 <= geocercaRadio, "P2 (20.30m) debe estar fuera de la geocerca de 15m.");
    }

    [Fact]
    public void GeofenceDiscrimination_25MeterGeofence_P1Inside_P2Inside()
    {
        var d01 = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P1_Lat, P1_Lon);
        var d02 = _service.CalcularDistanciaVincentyWgs84(P0_Lat, P0_Lon, P2_Lat, P2_Lon);

        double geocercaRadio = 25.0;
        Assert.True(d01 <= geocercaRadio, "P1 (9.38m) debe estar dentro de la geocerca de 25m.");
        Assert.True(d02 <= geocercaRadio, "P2 (20.30m) debe estar dentro de la geocerca de 25m.");
    }
}
