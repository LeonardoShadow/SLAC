namespace SLAC.Tests;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SLAC.Core.Security;
using Xunit;

public class CampusGeofenceTests
{
    private readonly VincentyGeodesicService _service;

    private const double CampusSurLat = -17.839210;
    private const double CampusSurLon = -63.194244;

    private const double CampusCentralLat = -17.776200;
    private const double CampusCentralLon = -63.195100;

    private const double LaPazLat = -16.500000;
    private const double LaPazLon = -68.150000;

    public CampusGeofenceTests()
    {
        var config = Options.Create(new CampusGeofenceConfig());
        _service = new VincentyGeodesicService(config, NullLogger<VincentyGeodesicService>.Instance);
    }

    [Fact]
    public void EstaDentroDeRegionMetropolitana_CampusSur_ReturnsTrue()
    {
        Assert.True(_service.EstaDentroDeRegionMetropolitana(CampusSurLat, CampusSurLon));
    }

    [Fact]
    public void EstaDentroDeRegionMetropolitana_CampusCentral_ReturnsTrue()
    {
        Assert.True(_service.EstaDentroDeRegionMetropolitana(CampusCentralLat, CampusCentralLon));
    }

    [Fact]
    public void EstaDentroDeRegionMetropolitana_OutsideSantaCruz_ReturnsFalse()
    {
        Assert.False(_service.EstaDentroDeRegionMetropolitana(LaPazLat, LaPazLon));
    }

    [Fact]
    public void EstaDentroDeRegionMetropolitana_InvalidCoordinates_ReturnsFalse()
    {
        Assert.False(_service.EstaDentroDeRegionMetropolitana(double.NaN, CampusSurLon));
        Assert.False(_service.EstaDentroDeRegionMetropolitana(CampusSurLat, double.PositiveInfinity));
    }

    [Fact]
    public void EstaDentroDeCampus_CampusSur_CoordinatesInside_ReturnsTrue()
    {
        Assert.True(_service.EstaDentroDeCampus(CampusSurLat, CampusSurLon, "UPDS_SUR"));
    }

    [Fact]
    public void EstaDentroDeCampus_CampusCentral_CoordinatesInside_ReturnsTrue()
    {
        Assert.True(_service.EstaDentroDeCampus(CampusCentralLat, CampusCentralLon, "UPDS_CENTRAL"));
    }

    [Fact]
    public void EstaDentroDeCampus_CrossCampusCheck_CampusCentralTestedAgainstCampusSur_ReturnsFalse()
    {
        // Las coordenadas de Campus Central (-17.7762) distan 6.97 km de Campus Sur
        Assert.False(_service.EstaDentroDeCampus(CampusCentralLat, CampusCentralLon, "UPDS_SUR"));
    }

    [Fact]
    public void EstaDentroDeCampus_CrossCampusCheck_CampusSurTestedAgainstCampusCentral_ReturnsFalse()
    {
        Assert.False(_service.EstaDentroDeCampus(CampusSurLat, CampusSurLon, "UPDS_CENTRAL"));
    }

    [Theory]
    [InlineData("UPDS_SUR", "UPDS_SUR")]
    [InlineData("upds_sur", "UPDS_SUR")]
    [InlineData("UPDS_CENTRAL", "UPDS_CENTRAL")]
    [InlineData("upds_central", "UPDS_CENTRAL")]
    public void ObtenerCampus_ValidCodes_ReturnsCorrectDefinition(string queryCode, string expectedCode)
    {
        var campus = _service.ObtenerCampus(queryCode);
        Assert.NotNull(campus);
        Assert.Equal(expectedCode, campus.Code, ignoreCase: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CAMPUS_INEXISTENTE")]
    public void ObtenerCampus_UnknownOrNullCode_FallsBackToDefaultCampus(string? queryCode)
    {
        var campus = _service.ObtenerCampus(queryCode);
        Assert.NotNull(campus);
        Assert.Equal("UPDS_SUR", campus.Code);
    }

    [Fact]
    public void CampusDefinition_PolygonRayCasting_AccurateInsideAndOutside()
    {
        var campusCustom = new CampusDefinition
        {
            Code = "CUSTOM_POLYGON",
            BoundingBox = new GeoBoundingBox(-17.850, -17.830, -63.200, -63.180),
            PolygonVertices = new List<GeoPolygonPoint>
            {
                new(-17.840, -63.195),
                new(-17.838, -63.190),
                new(-17.842, -63.190)
            }
        };

        // Punto centroide del triángulo: (-17.840, -63.1916)
        Assert.True(campusCustom.ContienePunto(-17.840, -63.1916));

        // Punto fuera del triángulo pero dentro del BoundingBox
        Assert.False(campusCustom.ContienePunto(-17.845, -63.195));
    }

    [Fact]
    public void DependencyInjection_Registration_CanBeResolvedFromServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<CampusGeofenceConfig>(_ => { });
        services.AddSingleton<IGeoCalculoService, VincentyGeodesicService>();

        using var provider = services.BuildServiceProvider();
        var geoService = provider.GetService<IGeoCalculoService>();

        Assert.NotNull(geoService);
        Assert.IsType<VincentyGeodesicService>(geoService);
    }
}
