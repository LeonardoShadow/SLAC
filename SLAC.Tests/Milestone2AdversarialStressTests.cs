namespace SLAC.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Configuration;
using SLAC.Core.Docentes.Entities;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Core.Institucional.Entities;
using SLAC.Core.Institucional.Repositories;
using SLAC.Core.Security;
using SLAC.Core.Security.Models;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using SLAC.Features.Attendance.Hubs;
using SLAC.Features.Attendance.Services;
using SLAC.Infrastructure.Redis;
using Xunit;

/// <summary>
/// Empirical Adversarial Stress Test Suite for Milestone 2.
/// Evaluates student attendance scan endpoints and SuscripcionService against:
/// 1. Bad / Expired / Null 3-Digit Visual PIN Challenge.
/// 2. Zero Spatial Variance (Synthetic Emulator Spoofing with Bessel correction).
/// 3. Non-monotonic / reversed timestamps and sampling rate violations.
/// 4. Out of bounds coordinates (outside UPDS campus polygon) & geofence violations.
/// 5. Clock skew > 8000ms (future / past timestamps).
/// 6. Kinematic impossible jumps (velocity gate threshold).
/// 7. Replay attacks (duplicate claims with same student ID).
/// 8. Device sharing conflicts (RN-05 anti-proxy rule).
/// 9. Real HTTP endpoint processing (POST /a/{sesionId}/confirmar-gps).
/// </summary>
public class Milestone2AdversarialStressTests : IAsyncDisposable
{
    private readonly Guid _institucionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const double CentralCampusLat = -17.776200;
    private const double CentralCampusLon = -63.195100;

    private readonly KeyManager _keyManager;
    private readonly TokenService _tokenService;
    private readonly VincentyGeodesicService _geodesicService;
    private readonly RedisSessionCacheRepository _sessionCache;
    private readonly AdversarialTestFakes _fakes;
    private readonly SuscripcionService _suscripcionService;

    private WebApplication? _testApp;
    private HttpClient? _httpClient;
    private string? _serverBaseUrl;

    public Milestone2AdversarialStressTests()
    {
        _keyManager = new KeyManager();
        _tokenService = new TokenService(_keyManager);

        var campusOptions = Options.Create(new CampusGeofenceConfig());
        _geodesicService = new VincentyGeodesicService(campusOptions);

        var redisOptions = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost:6379,abortConnect=false"
        });
        var redisProvider = new RedisConnectionProvider(redisOptions, NullLogger<RedisConnectionProvider>.Instance);
        _sessionCache = new RedisSessionCacheRepository(redisProvider, NullLogger<RedisSessionCacheRepository>.Instance);

        _fakes = new AdversarialTestFakes(_institucionId);
        _suscripcionService = new SuscripcionService(
            _tokenService,
            _fakes.ListaRepo,
            _fakes.DetalleRepo,
            _fakes.SuscripcionRepo,
            _fakes.EstudianteRepo,
            _fakes.DispositivoRepo,
            _fakes.RevinculacionRepo,
            _fakes.RevinculacionService,
            _fakes.MateriaRepo,
            _sessionCache,
            _fakes.EventBus,
            _fakes.NotificationService,
            NullLogger<SuscripcionService>.Instance,
            geoCalculoService: _geodesicService,
            campusConfigOptions: campusOptions);
    }

    public async ValueTask DisposeAsync()
    {
        if (_httpClient != null)
        {
            _httpClient.Dispose();
        }

        if (_testApp != null)
        {
            await _testApp.StopAsync();
            await _testApp.DisposeAsync();
        }
    }

    #region Helper Methods

    private async Task<(Guid sesionId, Guid materiaId, string qrToken)> SetupActiveGpsAlphaSessionAsync(
        string presenceCode = "842", double? docenteLat = null, double? docenteLon = null)
    {
        var sesionId = Guid.NewGuid();
        var materiaId = Guid.NewGuid();

        var materia = new Materia
        {
            Id = materiaId,
            InstitucionId = _institucionId,
            Codigo = "INF-321",
            Nombre = "Sistemas Distribuidos",
            HoraInicio = new TimeSpan(8, 0, 0),
            Dias = "L, M, X"
        };
        _fakes.MateriaRepo.Almacenar(materia);

        var lista = new ListaAsistencia
        {
            Id = sesionId,
            InstitucionId = _institucionId,
            MateriaId = materiaId,
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            HoraInicio = new TimeSpan(8, 0, 0),
            Estado = "Abierta"
        };
        _fakes.ListaRepo.Almacenar(lista);

        var state = new SessionEphemeralState
        {
            SesionId = sesionId,
            InstitucionId = _institucionId,
            MateriaId = materiaId,
            DocenteId = Guid.NewGuid(),
            InicioVigencia = DateTimeOffset.UtcNow,
            Vencimiento = DateTimeOffset.UtcNow.AddMinutes(20),
            ModoValidacionActivo = "gps_alpha",
            CodigoPresenciaActual = presenceCode,
            CampusCode = "UPDS_CENTRAL",
            DocenteLatitudAlpha = docenteLat ?? CentralCampusLat,
            DocenteLongitudAlpha = docenteLon ?? CentralCampusLon,
            DocentePrecisionAlpha = 4.0,
            RadioNominalAulaMetros = 12,
            EstaAbierta = true
        };

        await _sessionCache.SetActiveSessionAsync(state, TimeSpan.FromMinutes(20));
        var qrToken = _tokenService.GenerateSessionQrToken(sesionId, _institucionId, DateTimeOffset.UtcNow, 20, 0);

        return (sesionId, materiaId, qrToken);
    }

    private List<RawGpsTelemetrySample> GenerateRealisticSamples(
        int count = 10,
        double baseLat = CentralCampusLat,
        double baseLon = CentralCampusLon,
        double noiseMeters = 0.8,
        long? endTimeMs = null)
    {
        var endMs = endTimeMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var list = new List<RawGpsTelemetrySample>();
        var rand = new Random(1337);

        for (int i = 0; i < count; i++)
        {
            double dLat = (rand.NextDouble() - 0.5) * 2.0 * noiseMeters;
            double dLon = (rand.NextDouble() - 0.5) * 2.0 * noiseMeters;

            list.Add(new RawGpsTelemetrySample
            {
                Lat = baseLat + (dLat / GeoTelemetryValidator.MetersPerDegreeLat),
                Lon = baseLon + (dLon / GeoTelemetryValidator.MetersPerDegreeLon),
                Acc = 4.0 + (rand.NextDouble() * 1.5),
                Speed = 0.2 + (rand.NextDouble() * 0.3),
                TimestampMs = endMs - ((count - 1 - i) * 600) // 600ms delta (dentro de [150ms, 4000ms])
            });
        }
        return list;
    }

    private async Task EnsureTestServerRunningAsync()
    {
        if (_testApp != null) return;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("PublicScanPolicy", opt =>
            {
                opt.Window = TimeSpan.FromSeconds(10);
                opt.PermitLimit = 1000;
            });
        });

        builder.Services.AddRouting();
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton<ITokenService>(_tokenService);
        builder.Services.AddSingleton<IGeoCalculoService>(_geodesicService);
        builder.Services.AddSingleton<ISessionCacheRepository>(_sessionCache);
        builder.Services.AddSingleton<ISuscripcionService>(_suscripcionService);
        builder.Services.AddSingleton<IListaAsistenciaRepository>(_fakes.ListaRepo);
        builder.Services.AddSingleton<IMateriaRepository>(_fakes.MateriaRepo);
        builder.Services.AddSingleton<IDispositivoRepository>(_fakes.DispositivoRepo);
        builder.Services.AddSingleton<IEstudianteRepository>(_fakes.EstudianteRepo);
        builder.Services.AddSingleton<IAsistenciaDetalleRepository>(_fakes.DetalleRepo);
        builder.Services.AddSingleton<IRevinculacionRepository>(_fakes.RevinculacionRepo);
        builder.Services.AddSingleton<IClassroomNotificationService>(_fakes.NotificationService);
        builder.Services.AddSingleton<IEspacioRepository>(_fakes.EspacioRepo);

        _testApp = builder.Build();
        AttendanceStudentEndpoints.MapAttendanceStudentEndpoints(_testApp);
        await _testApp.StartAsync();
        var dsList = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)_testApp).DataSources;
        var epList = dsList.SelectMany(d => d.Endpoints).Select(e => e.DisplayName).ToList();
        var routePatterns = $"DS count: {dsList.Count} | EPs: {string.Join(" ; ", epList)}";
        var server = _testApp.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addresses = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        _serverBaseUrl = addresses!.Addresses.First();
        if (!_serverBaseUrl.EndsWith("/")) _serverBaseUrl += "/";
        _httpClient = new HttpClient { BaseAddress = new Uri(_serverBaseUrl) };
        _routesDebug = routePatterns;
    }

    private string _routesDebug = "";

    #endregion

    #region 1. Hostile 3-Digit Visual Presence PIN Challenge Tests

    [Fact]
    public async Task PIN_WrongSubmittedCode_ShouldRejectStrictlyWithPresenciaError()
    {
        // Arrange
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "842");

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            Codigo = "EST-PIN-01",
            Nombres = "Marcos",
            Apellidos = "Pérez",
            Correo = "marcos@upds.edu.bo",
            AceptaTerminos = true,
            UserAgent = "Mozilla/5.0 (iPhone)",
            Modo = "gps_alpha",
            DynamicPresenceCode = "999", // PIN hostil erróneo
            Latitud = CentralCampusLat,
            Longitud = CentralCampusLon,
            PrecisionGpsMetros = 4.5
        };

        // Act
        var result = await _suscripcionService.ProcesarEscaneoAsync(request);

        // Assert
        Assert.False(result.Exito);
        Assert.Contains("Código de presencia visual incorrecto o expirado", result.MensajeError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PIN_MissingOrWhitespaceCode_ShouldRejectStrictly(string? blankPin)
    {
        // Arrange
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "357");

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            Codigo = "EST-PIN-BLANK",
            Nombres = "Estudiante",
            Apellidos = "SinPin",
            Correo = "sinpin@upds.edu.bo",
            AceptaTerminos = true,
            UserAgent = "Mozilla/5.0 (Android)",
            Modo = "gps_alpha",
            DynamicPresenceCode = blankPin,
            Latitud = CentralCampusLat,
            Longitud = CentralCampusLon,
            PrecisionGpsMetros = 4.0
        };

        // Act
        var result = await _suscripcionService.ProcesarEscaneoAsync(request);

        // Assert
        Assert.False(result.Exito);
        Assert.Contains("Código de presencia visual incorrecto o expirado", result.MensajeError);
    }

    [Fact]
    public async Task PIN_ExpiredRotatedCode_ShouldRejectPreviousEpochPIN()
    {
        // Arrange: Sesión inicia con PIN "111", rota a "222"
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "111");

        // Simular rotación del PIN docente en el estado efímero
        var sessionState = await _sessionCache.GetActiveSessionAsync(sesionId);
        Assert.NotNull(sessionState);
        sessionState.CodigoPresenciaActual = "222"; // Nuevo PIN vigente en pantalla
        sessionState.RotacionIndex = 1;
        await _sessionCache.SetActiveSessionAsync(sessionState, TimeSpan.FromMinutes(20));

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            Codigo = "EST-PIN-EXPIRED",
            Nombres = "Estudiante",
            Apellidos = "Lento",
            Correo = "lento@upds.edu.bo",
            AceptaTerminos = true,
            UserAgent = "Mozilla/5.0 (iPhone)",
            Modo = "gps_alpha",
            DynamicPresenceCode = "111", // PIN viejo expirado
            Latitud = CentralCampusLat,
            Longitud = CentralCampusLon,
            PrecisionGpsMetros = 4.0
        };

        // Act
        var result = await _suscripcionService.ProcesarEscaneoAsync(request);

        // Assert
        Assert.False(result.Exito);
        Assert.Contains("Código de presencia visual incorrecto o expirado", result.MensajeError);
    }

    [Fact]
    public async Task PIN_MatchingCode_ShouldAcceptAndRecordAttendance()
    {
        // Arrange
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "842");

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            Codigo = "EST-PIN-OK",
            Nombres = "Estudiante",
            Apellidos = "Legitimo",
            Correo = "legitimo@upds.edu.bo",
            AceptaTerminos = true,
            UserAgent = "Mozilla/5.0 (iPhone)",
            Modo = "gps_alpha",
            DynamicPresenceCode = "842", // PIN exacto proyectado
            Latitud = CentralCampusLat,
            Longitud = CentralCampusLon,
            PrecisionGpsMetros = 4.0
        };

        // Act
        var result = await _suscripcionService.ProcesarEscaneoAsync(request);

        // Assert
        Assert.True(result.Exito, result.MensajeError);
        Assert.Equal("EST-PIN-OK", result.EstudianteCodigo);
    }

    #endregion

    #region 2. Zero Spatial Variance (Synthetic Emulator Spoofing)

    [Fact]
    public void ZeroVariance_ExactDuplicateCoordinatesAcrossSamples_RejectsWithVarianzaCero()
    {
        // Arrange: 8 muestras exactamente con las mismas coordenadas (típico de GPS Fake / Joystick)
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = new List<RawGpsTelemetrySample>();
        for (int i = 0; i < 8; i++)
        {
            samples.Add(new RawGpsTelemetrySample
            {
                Lat = CentralCampusLat,
                Lon = CentralCampusLon,
                Acc = 3.5,
                Speed = 0.0,
                TimestampMs = now - ((7 - i) * 500)
            });
        }

        // Act
        var validation = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, now);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Falsificación detectada (Varianza Cero)", validation.RejectReason);
    }

    [Fact]
    public void ZeroVariance_BesselStdDevBelowNoiseFloor_RejectsWithLimiteInstrumental()
    {
        // Arrange: Jitter microscópico artificial (0.002m = 2mm de dispersión, < piso térmico 0.05m)
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = new List<RawGpsTelemetrySample>();
        for (int i = 0; i < 7; i++)
        {
            double artificialJitterMeters = i * 0.002;
            samples.Add(new RawGpsTelemetrySample
            {
                Lat = CentralCampusLat + (artificialJitterMeters / GeoTelemetryValidator.MetersPerDegreeLat),
                Lon = CentralCampusLon,
                Acc = 4.0,
                Speed = 0.0,
                TimestampMs = now - ((6 - i) * 600)
            });
        }

        // Act
        var validation = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, now);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("inferior al límite instrumental", validation.RejectReason);
    }

    #endregion

    #region 3. Telemetry Timestamp Non-Monotonicity & Sampling Rate

    [Fact]
    public void Temporal_ReversalNegativeDelta_RejectsStrictly()
    {
        // Arrange: Muestra 3 retrocede 400ms en el tiempo respecto a la muestra 2
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = GenerateRealisticSamples(6, endTimeMs: now);
        samples[3].TimestampMs = samples[2].TimestampMs - 400;

        // Act
        var validation = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, now);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Inconsistencia temporal", validation.RejectReason);
        Assert.Contains("timestamp retrocede", validation.RejectReason);
    }

    [Fact]
    public void Temporal_SubPhysicalFrequency_Under150ms_RejectsSynthetics()
    {
        // Arrange: Frecuencia de 70ms (< 150ms límite físico del chipset GNSS móvil)
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = GenerateRealisticSamples(5, endTimeMs: now);
        samples[2].TimestampMs = samples[1].TimestampMs + 70;

        // Act
        var validation = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, now);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Frecuencia no física", validation.RejectReason);
    }

    [Fact]
    public void Temporal_ExcessiveGap_Over4000ms_Rejects()
    {
        // Arrange: Brecha de 5000ms (> 4000ms) entre lecturas
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = GenerateRealisticSamples(5, endTimeMs: now);
        samples[3].TimestampMs = samples[2].TimestampMs + 5000;

        // Act
        var validation = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, now);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Intervalo excesivo", validation.RejectReason);
    }

    #endregion

    #region 4. Clock Skew & Future/Past Timestamps

    [Fact]
    public void ClockSkew_FutureTimestampExceeding8000ms_Rejects()
    {
        // Arrange: Telemetría fechada 15 segundos en el futuro respecto al reloj servidor
        var serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var futureSamples = GenerateRealisticSamples(6, endTimeMs: serverNow + 15000);

        // Act
        var validation = GeoTelemetryValidator.ValidateAndDeriveLocation(futureSamples, serverNow);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Desfase de reloj con el servidor excesivo", validation.RejectReason);
    }

    [Fact]
    public void ClockSkew_DistantPastTimestampExceeding8000ms_Rejects()
    {
        // Arrange: Telemetría rezagada 20 segundos en el pasado
        var serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pastSamples = GenerateRealisticSamples(6, endTimeMs: serverNow - 20000);

        // Act
        var validation = GeoTelemetryValidator.ValidateAndDeriveLocation(pastSamples, serverNow);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Desfase de reloj con el servidor excesivo", validation.RejectReason);
    }

    #endregion

    #region 5. Out of Bounds Coordinates & Geofencing

    [Fact]
    public async Task Geofence_OutsideCampusPolygon_RejectsWithLimitesPerimetrales()
    {
        // Arrange: Coordenadas en el centro urbano de Santa Cruz (-17.785, -63.180), fuera de UPDS
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "555");

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            Codigo = "EST-CAMPUS-OUT",
            Nombres = "Estudiante",
            Apellidos = "EnCasa",
            Correo = "casa@upds.edu.bo",
            AceptaTerminos = true,
            UserAgent = "Mozilla/5.0 (iPhone)",
            Modo = "gps_alpha",
            DynamicPresenceCode = "555",
            Latitud = -17.785000,
            Longitud = -63.180000,
            PrecisionGpsMetros = 5.0
        };

        // Act
        var result = await _suscripcionService.ProcesarEscaneoAsync(request);

        // Assert
        Assert.False(result.Exito);
        Assert.Contains("fuera de los límites perimetrales autorizados", result.MensajeError);
    }

    [Fact]
    public async Task Geofence_InsideCampus_OutsideClassroomAdaptiveRadius_RejectsWithFueraDeRango()
    {
        // Arrange: Coordenadas dentro de UPDS Central, pero a 65 metros del aula del docente
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "777");

        // Desplazamiento de 65 metros al este (mayor que el radio adaptativo máximo de 28m)
        double offsetLon = 65.0 / GeoTelemetryValidator.MetersPerDegreeLon;

        var request = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            Codigo = "EST-COURTYARD",
            Nombres = "Estudiante",
            Apellidos = "EnPasillo",
            Correo = "pasillo@upds.edu.bo",
            AceptaTerminos = true,
            UserAgent = "Mozilla/5.0 (Android)",
            Modo = "gps_alpha",
            DynamicPresenceCode = "777",
            Latitud = CentralCampusLat,
            Longitud = CentralCampusLon + offsetLon,
            PrecisionGpsMetros = 4.0
        };

        // Act
        var result = await _suscripcionService.ProcesarEscaneoAsync(request);

        // Assert
        Assert.False(result.Exito);
        Assert.Contains("Ubicación fuera de rango", result.MensajeError);
        Assert.NotNull(result.DistanciaCalculadaMetros);
        Assert.True(result.DistanciaCalculadaMetros.Value > 28.0);
    }

    #endregion

    #region 6. Kinematic Impossible Jump (Velocity Gate)

    [Fact]
    public void Kinematic_TeleportJumpExceedingStochasticThreshold_RejectsWithSaltoCinematico()
    {
        // Arrange: Salto de 90 metros en 1.0 segundo con alta precisión de 3 metros
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = GenerateRealisticSamples(6, endTimeMs: now);
        samples[4].Lat = samples[3].Lat + (90.0 / GeoTelemetryValidator.MetersPerDegreeLat);
        samples[4].Acc = 3.0;
        samples[3].Acc = 3.0;
        samples[4].TimestampMs = samples[3].TimestampMs + 1000;

        // Act
        var validation = GeoTelemetryValidator.ValidateAndDeriveLocation(samples, now);

        // Assert
        Assert.False(validation.IsValid);
        Assert.Contains("Salto cinemático anómalo", validation.RejectReason);
    }

    #endregion

    #region 7. Replay Attacks & Idempotent Duplicate Claims

    [Fact]
    public async Task Replay_DuplicateClaim_SameStudent_ReturnsAlreadyRegisteredIdempotent()
    {
        // Arrange
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "654");

        var studentId = Guid.NewGuid();
        var estudiante = new Estudiante
        {
            Id = studentId,
            InstitucionId = _institucionId,
            Codigo = "EST-REPLAY-01",
            Nombres = "Carlos",
            Apellidos = "Replay",
            Correo = "carlos@upds.edu.bo",
            ConsentimientoEn = DateTimeOffset.UtcNow
        };
        await _fakes.EstudianteRepo.CrearOActualizarAsync(estudiante);

        var dispId = Guid.NewGuid();
        var (deviceCookie, _) = _tokenService.GenerateDeviceCredential(dispId, _institucionId);
        await _fakes.DispositivoRepo.RegistrarDispositivoAsync(new Dispositivo
        {
            Id = dispId,
            Kid = "slac-key-v1",
            JtiHash = "hash-replay-01"
        }, studentId, _institucionId);

        var firstRequest = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            DeviceToken = deviceCookie,
            Modo = "gps_alpha",
            DynamicPresenceCode = "654",
            Latitud = CentralCampusLat,
            Longitud = CentralCampusLon,
            PrecisionGpsMetros = 4.0
        };

        // Act 1: Primer reclamo legítimo
        var firstResult = await _suscripcionService.ProcesarEscaneoAsync(firstRequest);
        Assert.True(firstResult.Exito, firstResult.MensajeError);
        Assert.False(firstResult.YaRegistradoHoy);

        // Act 2: Replay del escaneo por el mismo alumno (segunda petición)
        var replayResult = await _suscripcionService.ProcesarEscaneoAsync(firstRequest);

        // Assert
        Assert.True(replayResult.Exito, "El replay debe devolver éxito idempotente.");
        Assert.True(replayResult.YaRegistradoHoy, "Debe marcar explícitamente YaRegistradoHoy.");
        Assert.Equal(firstResult.EstudianteId, replayResult.EstudianteId);

        // Verificar que el contador de asistentes en sesión no se infló
        var state = await _sessionCache.GetActiveSessionAsync(sesionId);
        Assert.NotNull(state);
        Assert.Equal(1, state.ContadorAsistentes);
    }

    #endregion

    #region 8. Device Sharing Conflicts (RN-05 Anti-Proxy Rule)

    [Fact]
    public async Task DeviceSharing_RN05_SecondStudentOnSameDevice_RejectsWithConflictoDeDispositivo()
    {
        // Arrange
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "432");

        // Estudiante 1 con su celular
        var estudiante1 = new Estudiante
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            Codigo = "EST-PHONE-01",
            Nombres = "Estudiante",
            Apellidos = "Primero",
            Correo = "primero@upds.edu.bo"
        };
        await _fakes.EstudianteRepo.CrearOActualizarAsync(estudiante1);

        var sharedDeviceId = Guid.NewGuid();
        var (sharedDeviceCookie, _) = _tokenService.GenerateDeviceCredential(sharedDeviceId, _institucionId);
        await _fakes.DispositivoRepo.RegistrarDispositivoAsync(new Dispositivo
        {
            Id = sharedDeviceId,
            Kid = "slac-key-v1",
            JtiHash = "hash-shared-phone"
        }, estudiante1.Id, _institucionId);

        // Reclamo 1: Estudiante 1 registra asistencia en su celular
        var req1 = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            DeviceToken = sharedDeviceCookie,
            Modo = "gps_alpha",
            DynamicPresenceCode = "432",
            Latitud = CentralCampusLat,
            Longitud = CentralCampusLon,
            PrecisionGpsMetros = 4.0
        };
        var res1 = await _suscripcionService.ProcesarEscaneoAsync(req1);
        Assert.True(res1.Exito, res1.MensajeError);

        // Estudiante 2 intenta registrarse usando la misma cookie / dispositivo prestado
        var claimResultDirect = await _sessionCache.TryClaimAttendanceAsync(
            sesionId,
            Guid.NewGuid(), // Segundo estudiante diferente
            sharedDeviceId.ToString(), // Mismo dispositivo celular
            "432",
            validatePresenceCode: true);

        // Assert
        Assert.False(claimResultDirect.IsSuccess);
        Assert.Equal(AttendanceClaimStatus.DeviceConflict, claimResultDirect.Status);
        Assert.Contains("RN-05", claimResultDirect.ErrorMessage);
    }

    #endregion

    #region 9. Minimal API HTTP Endpoints Integration (POST /a/{sesionId}/confirmar-gps)

    [Fact]
    public async Task Endpoint_ConfirmarGps_WithHostileZeroVarianceJson_ReturnsBadRequestTelemetryRejected()
    {
        // Arrange
        await EnsureTestServerRunningAsync();
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "999");

        // Construir 8 muestras idénticas (Varianza Cero)
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var hostileSamples = new List<RawGpsTelemetrySample>();
        for (int i = 0; i < 8; i++)
        {
            hostileSamples.Add(new RawGpsTelemetrySample
            {
                Lat = CentralCampusLat,
                Lon = CentralCampusLon,
                Acc = 4.0,
                Speed = 0.0,
                TimestampMs = now - ((7 - i) * 500)
            });
        }

        var payload = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            Modo = "gps_alpha",
            DynamicPresenceCode = "999",
            Samples = hostileSamples
        };

        // Act: POST a /a/{sesionId}/confirmar-gps
        var response = await _httpClient!.PostAsJsonAsync($"/a/{sesionId}/confirmar-gps", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("TELEMETRY_REJECTED", body);
        Assert.Contains("Varianza Cero", body);
    }

    [Fact]
    public async Task Endpoint_ConfirmarGps_WithClockSkewJson_ReturnsBadRequestTelemetryRejected()
    {
        // Arrange
        await EnsureTestServerRunningAsync();
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "999");

        // Muestras fechadas 25 segundos en el futuro
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var hostileSamples = GenerateRealisticSamples(6, endTimeMs: now + 25000);

        var payload = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            Modo = "gps_alpha",
            DynamicPresenceCode = "999",
            Samples = hostileSamples
        };

        // Act
        var response = await _httpClient!.PostAsJsonAsync($"/a/{sesionId}/confirmar-gps", payload);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("TELEMETRY_REJECTED", body);
        Assert.Contains("Desfase de reloj", body);
    }

    [Fact]
    public async Task Endpoint_ConfirmarGps_WithWrongPinJson_ReturnsBadRequestPresenceCodeError()
    {
        // Arrange
        await EnsureTestServerRunningAsync();
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "842");

        // Muestras físicas válidas
        var validSamples = GenerateRealisticSamples(8);

        // Estudiante identificado con cookie de dispositivo
        var studentId = Guid.NewGuid();
        var estudiante = new Estudiante
        {
            Id = studentId,
            InstitucionId = _institucionId,
            Codigo = "EST-HTTP-PIN",
            Nombres = "Estudiante",
            Apellidos = "Endpoint",
            Correo = "endpin@upds.edu.bo"
        };
        await _fakes.EstudianteRepo.CrearOActualizarAsync(estudiante);

        var dispId = Guid.NewGuid();
        var (deviceCookie, _) = _tokenService.GenerateDeviceCredential(dispId, _institucionId);
        await _fakes.DispositivoRepo.RegistrarDispositivoAsync(new Dispositivo
        {
            Id = dispId,
            Kid = "slac-key-v1",
            JtiHash = "hash-http-pin"
        }, studentId, _institucionId);

        var payload = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            DeviceToken = deviceCookie,
            Modo = "gps_alpha",
            DynamicPresenceCode = "111", // PIN incorrecto ("842" requerido)
            Samples = validSamples
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/a/{sesionId}/confirmar-gps")
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Add("Cookie", $"slac_device={deviceCookie}");

        // Act
        var response = await _httpClient!.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Código de presencia visual incorrecto o expirado", body);
    }

    [Fact]
    public async Task Endpoint_ConfirmarGps_WithValidTelemetryAndPin_ReturnsSuccess200()
    {
        // Arrange
        await EnsureTestServerRunningAsync();
        var (sesionId, _, qrToken) = await SetupActiveGpsAlphaSessionAsync(presenceCode: "842");

        var validSamples = GenerateRealisticSamples(8);

        var studentId = Guid.NewGuid();
        var estudiante = new Estudiante
        {
            Id = studentId,
            InstitucionId = _institucionId,
            Codigo = "EST-HTTP-OK",
            Nombres = "Estudiante",
            Apellidos = "Aprobado",
            Correo = "ok@upds.edu.bo"
        };
        await _fakes.EstudianteRepo.CrearOActualizarAsync(estudiante);

        var dispId = Guid.NewGuid();
        var (deviceCookie, _) = _tokenService.GenerateDeviceCredential(dispId, _institucionId);
        await _fakes.DispositivoRepo.RegistrarDispositivoAsync(new Dispositivo
        {
            Id = dispId,
            Kid = "slac-key-v1",
            JtiHash = "hash-http-ok"
        }, studentId, _institucionId);

        var payload = new AttendanceScanRequest
        {
            SesionId = sesionId,
            QrToken = qrToken,
            DeviceToken = deviceCookie,
            Modo = "gps_alpha",
            DynamicPresenceCode = "842", // PIN exacto
            Samples = validSamples
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/a/{sesionId}/confirmar-gps")
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Add("Cookie", $"slac_device={deviceCookie}");

        // Act
        var response = await _httpClient!.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var scanRes = JsonSerializer.Deserialize<AttendanceScanResult>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(scanRes);
        Assert.True(scanRes.Exito);
        Assert.True(scanRes.EstudianteCodigo == "EST-HTTP-OK" || scanRes.EstudianteCodigo == "EST-AUTO");
    }

    #endregion

    #region Internal Fakes Helper

    private sealed class AdversarialTestFakes(Guid institucionId)
    {
        public Guid InstitucionId { get; } = institucionId;
        public FakeListaAsistenciaRepo ListaRepo { get; } = new();
        public FakeAsistenciaDetalleRepo DetalleRepo { get; } = new();
        public FakeSuscripcionRepo SuscripcionRepo { get; } = new();
        public FakeEstudianteRepo EstudianteRepo { get; } = new();
        public FakeDispositivoRepo DispositivoRepo { get; } = new();
        public FakeRevinculacionRepo RevinculacionRepo { get; } = new();
        public FakeRevinculacionServ RevinculacionService { get; } = new();
        public FakeMateriaRepo MateriaRepo { get; } = new();
        public FakeSessionEventBus EventBus { get; } = new();
        public FakeClassroomNotificationServ NotificationService { get; } = new();
        public FakeEspacioRepo EspacioRepo { get; } = new();
    }

    private sealed class FakeEspacioRepo : IEspacioRepository
    {
        public Task<List<Espacio>> GetByInstitucionAsync(Guid institucionId, CancellationToken cancellationToken = default) => Task.FromResult(new List<Espacio>());
        public Task<Espacio?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Espacio?>(null);
        public Task<Espacio> CreateAsync(Espacio espacio, CancellationToken cancellationToken = default) => Task.FromResult(espacio);
        public Task<Espacio> UpdateAsync(Espacio espacio, CancellationToken cancellationToken = default) => Task.FromResult(espacio);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeListaAsistenciaRepo : IListaAsistenciaRepository
    {
        private readonly List<ListaAsistencia> _items = [];

        public void Almacenar(ListaAsistencia item) => _items.Add(item);

        public Task<ListaAsistencia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));

        public Task<ListaAsistencia?> ObtenerPorMateriaYFechaAsync(Guid materiaId, DateOnly fecha, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.MateriaId == materiaId && x.Fecha == fecha));

        public Task<IReadOnlyList<ListaAsistencia>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ListaAsistencia>>([.. _items.Where(x => x.MateriaId == materiaId)]);

        public Task<ListaAsistencia> CrearOActualizarAsync(ListaAsistencia lista, CancellationToken ct = default)
        {
            _items.Add(lista);
            return Task.FromResult(lista);
        }

        public Task ActualizarEstadoAsync(Guid id, string nuevoEstado, TimeSpan? horaCierre = null, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null) item.Estado = nuevoEstado;
            return Task.CompletedTask;
        }

        public Task ActualizarTotalesAsync(Guid id, int totalSuscritos, int totalPresentes, int totalFaltas, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.TotalSuscritos = totalSuscritos;
                item.TotalPresentes = totalPresentes;
                item.TotalFaltas = totalFaltas;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAsistenciaDetalleRepo : IAsistenciaDetalleRepository
    {
        private readonly List<AsistenciaDetalle> _items = [];

        public Task<IReadOnlyList<AsistenciaDetalle>> ListarPorListaAsync(Guid listaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AsistenciaDetalle>>([.. _items.Where(x => x.ListaId == listaId)]);

        public Task<AsistenciaDetalle?> ObtenerPorListaYEstudianteAsync(Guid listaId, Guid estudianteId, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.ListaId == listaId && x.EstudianteId == estudianteId));

        public Task<AsistenciaDetalle> RegistrarAsistenciaAsync(AsistenciaDetalle detalle, CancellationToken ct = default)
        {
            var existing = _items.FirstOrDefault(x => x.ListaId == detalle.ListaId && x.EstudianteId == detalle.EstudianteId);
            if (existing != null) _items.Remove(existing);
            _items.Add(detalle);
            return Task.FromResult(detalle);
        }

        public Task EliminarPorListaAsync(Guid listaId, CancellationToken ct = default)
        {
            _items.RemoveAll(x => x.ListaId == listaId);
            return Task.CompletedTask;
        }

        public Task<int> RegistrarFaltasIdempotenteAsync(Guid listaId, Guid institucionId, IEnumerable<Guid> estudiantesIds, CancellationToken ct = default)
        {
            var count = 0;
            foreach (var id in estudiantesIds)
            {
                if (!_items.Any(x => x.ListaId == listaId && x.EstudianteId == id))
                {
                    _items.Add(new AsistenciaDetalle
                    {
                        ListaId = listaId,
                        EstudianteId = id,
                        InstitucionId = institucionId,
                        Estado = "Falta",
                        Origen = "Cierre"
                    });
                    count++;
                }
            }
            return Task.FromResult(count);
        }
    }

    private sealed class FakeSuscripcionRepo : ISuscripcionRepository
    {
        private readonly List<Suscripcion> _items = [];

        public Task<IReadOnlyList<Suscripcion>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Suscripcion>>([.. _items.Where(x => x.MateriaId == materiaId)]);

        public Task<IReadOnlyList<Guid>> ListarEstudiantesIdsPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([.. _items.Where(x => x.MateriaId == materiaId).Select(x => x.EstudianteId)]);

        public Task<Suscripcion?> ObtenerAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId));

        public Task<Suscripcion> SuscribirAsync(Suscripcion suscripcion, CancellationToken ct = default)
        {
            if (!_items.Any(x => x.MateriaId == suscripcion.MateriaId && x.EstudianteId == suscripcion.EstudianteId))
            {
                _items.Add(suscripcion);
            }
            return Task.FromResult(suscripcion);
        }

        public Task<bool> DesinscribirAsync(Guid materiaId, Guid estudianteId, CancellationToken ct = default)
        {
            var match = _items.FirstOrDefault(x => x.MateriaId == materiaId && x.EstudianteId == estudianteId);
            if (match != null)
            {
                _items.Remove(match);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    private sealed class FakeEstudianteRepo : IEstudianteRepository
    {
        private readonly List<Estudiante> _items = [];

        public Task<Estudiante?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));

        public Task<Estudiante?> ObtenerPorCodigoAsync(Guid institucionId, string codigo, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.InstitucionId == institucionId && x.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase)));

        public Task<Estudiante?> ObtenerPorCorreoAsync(Guid institucionId, string correo, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.InstitucionId == institucionId && x.Correo.Equals(correo, StringComparison.OrdinalIgnoreCase)));

        public Task<Estudiante> CrearOActualizarAsync(Estudiante estudiante, CancellationToken ct = default)
        {
            var existing = _items.FirstOrDefault(x => x.Id == estudiante.Id || (x.InstitucionId == estudiante.InstitucionId && x.Codigo.Equals(estudiante.Codigo, StringComparison.OrdinalIgnoreCase)));
            if (existing != null) _items.Remove(existing);
            _items.Add(estudiante);
            return Task.FromResult(estudiante);
        }

        public Task<IReadOnlyList<Estudiante>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Estudiante>>([.. _items.Where(x => x.InstitucionId == institucionId)]);

        public Task<bool> EliminarAsync(Guid id, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                _items.Remove(item);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    private sealed class FakeDispositivoRepo : IDispositivoRepository
    {
        private readonly List<Dispositivo> _dispositivos = [];
        private readonly List<DispositivoEstudiante> _vinculaciones = [];

        public Task<Dispositivo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_dispositivos.FirstOrDefault(x => x.Id == id));

        public Task<Dispositivo?> ObtenerPorJtiHashAsync(string jtiHash, CancellationToken ct = default)
            => Task.FromResult(_dispositivos.FirstOrDefault(x => x.JtiHash == jtiHash));

        public Task<Dispositivo> RegistrarDispositivoAsync(Dispositivo dispositivo, Guid estudianteId, Guid institucionId, CancellationToken ct = default)
        {
            _dispositivos.Add(dispositivo);
            _vinculaciones.Add(new DispositivoEstudiante
            {
                Id = Guid.NewGuid(),
                DispositivoId = dispositivo.Id,
                EstudianteId = estudianteId,
                InstitucionId = institucionId,
                Activo = true
            });
            return Task.FromResult(dispositivo);
        }

        public Task<Estudiante?> ObtenerEstudiantePorDispositivoAsync(Guid dispositivoId, Guid institucionId, CancellationToken ct = default)
        {
            var vinc = _vinculaciones.FirstOrDefault(x => x.DispositivoId == dispositivoId && x.InstitucionId == institucionId && x.Activo);
            if (vinc == null) return Task.FromResult<Estudiante?>(null);

            return Task.FromResult<Estudiante?>(new Estudiante
            {
                Id = vinc.EstudianteId,
                InstitucionId = institucionId,
                Codigo = "EST-AUTO",
                Nombres = "Auto",
                Apellidos = "Estudiante",
                Correo = "auto@upds.edu.bo"
            });
        }

        public Task<DispositivoEstudiante?> ObtenerVinculacionActivaAsync(Guid estudianteId, Guid institucionId, CancellationToken ct = default)
            => Task.FromResult(_vinculaciones.FirstOrDefault(x => x.EstudianteId == estudianteId && x.InstitucionId == institucionId && x.Activo));

        public Task DesactivarVinculacionesAsync(Guid estudianteId, Guid institucionId, CancellationToken ct = default)
        {
            foreach (var v in _vinculaciones.Where(x => x.EstudianteId == estudianteId && x.InstitucionId == institucionId))
            {
                v.Activo = false;
            }
            return Task.CompletedTask;
        }

        public Task ActualizarUltimoUsoAsync(Guid dispositivoId, CancellationToken ct = default)
        {
            var item = _dispositivos.FirstOrDefault(x => x.Id == dispositivoId);
            if (item != null) item.UltimoUsoEn = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task RevocarDispositivoAsync(Guid dispositivoId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeRevinculacionRepo : IRevinculacionRepository
    {
        private readonly List<Revinculacion> _items = [];

        public Task<Revinculacion> CrearSolicitudAsync(Revinculacion solicitud, CancellationToken ct = default)
        {
            _items.Add(solicitud);
            return Task.FromResult(solicitud);
        }

        public Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Revinculacion>>([.. _items.Where(x => x.MateriaId == materiaId && x.EstaPendiente)]);

        public Task<Revinculacion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));

        public Task<Revinculacion?> ObtenerPendientePorEstudianteYMateriaAsync(Guid estudianteId, Guid materiaId, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.EstudianteId == estudianteId && x.MateriaId == materiaId && x.EstaPendiente));

        public Task MarcarComoUsadaAsync(Guid id, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null) item.UsadaEn = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRevinculacionServ : IRevinculacionService
    {
        public Task<SolicitudRevinculacionResult> SolicitarRevinculacionAsync(Guid sesionId, string codigoEstudiante, string userAgent, CancellationToken ct = default)
            => Task.FromResult(new SolicitudRevinculacionResult(true, false, Guid.NewGuid(), "Solicitud pendiente.", "Alumno", codigoEstudiante));

        public Task<(bool Exito, string? Error)> AutorizarRevinculacionAsync(Guid solicitudId, string actorDocente, CancellationToken ct = default)
            => Task.FromResult<(bool, string?)>((true, null));

        public Task<IReadOnlyList<Revinculacion>> ListarPendientesPorMateriaAsync(Guid materiaId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Revinculacion>>([]);
    }

    private sealed class FakeMateriaRepo : IMateriaRepository
    {
        private readonly List<Materia> _items = [];

        public void Almacenar(Materia item) => _items.Add(item);

        public Task<Materia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<Materia>> ListarPorDocenteAsync(Guid docenteId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Materia>>([.. _items.Where(x => x.DocenteId == docenteId)]);

        public Task<IReadOnlyList<Materia>> ListarActivasPorDiaAsync(Guid institucionId, string diaSigla, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Materia>>([.. _items.Where(x => x.InstitucionId == institucionId)]);

        public Task<IReadOnlyList<Materia>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Materia>>([.. _items.Where(x => x.InstitucionId == institucionId)]);

        public Task<bool> ArchivarAsync(Guid id, CancellationToken ct = default)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null) item.Estado = "archivada";
            return Task.FromResult(true);
        }

        public Task<Materia> GuardarAsync(Materia materia, CancellationToken ct = default)
        {
            _items.Add(materia);
            return Task.FromResult(materia);
        }
    }

    private sealed class FakeSessionEventBus : ISessionEventBus
    {
        public Task PublishEventAsync(AttendanceEventMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SubscribeToSessionEventsAsync(Guid sesionId, Func<AttendanceEventMessage, Task> handler, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UnsubscribeFromSessionEventsAsync(Guid sesionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeClassroomNotificationServ : IClassroomNotificationService
    {
        public Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string? estudianteCodigo, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyAttendanceRecordedAsync(Guid sesionId, int totalPresentes, string estudianteNombre, string? estudianteCodigo, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyQrRotatedAsync(Guid sesionId, string nuevoToken, int rotacionIndex, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifySessionClosedAsync(Guid sesionId, int totalPresentes, int totalFaltas, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}
