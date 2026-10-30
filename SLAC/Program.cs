using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using MudBlazor.Services;
// Core y Módulos de SLAC
using SLAC.Components;
using SLAC.Core.Security;
using SLAC.Features.Attendance.Background;
using SLAC.Features.Attendance.Hubs;
using SLAC.Infrastructure.Data;
using SLAC.Infrastructure.Redis;
using SLAC.Infrastructure.Repositories;
using SLAC.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor UI Services
builder.Services.AddMudServices();

// Autenticación y Autorización perimetral defensiva (VULN-05 y VULN-09)
builder.Services.AddAuthentication("SLACAuthScheme")
    .AddCookie("SLACCookies", options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "slac_auth_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddScheme<AuthenticationSchemeOptions, SLACAuthenticationHandler>("SLACAuthScheme", options => { });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder("SLACAuthScheme", "SLACCookies")
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddCascadingAuthenticationState();

// Rate Limiting defensivo contra abusos de escaneo y fuerza bruta (VULN-10)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("PublicScanPolicy", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(10);
        opt.PermitLimit = 40;
        opt.QueueLimit = 0;
    });
});

// Core Security & Criptografía (ES256)
builder.Services.AddSingleton<IKeyManager, KeyManager>();
builder.Services.AddSingleton<ITokenService, TokenService>();

// Abstracción Temporal y Multi-Tenancy (REF-10)
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SLAC.Core.Institucional.Services.IInstitutionalTimeProvider, SLAC.Infrastructure.Services.InstitutionalTimeProvider>();

// Infraestructura de Datos y Repositorios
builder.Services.AddSupabaseInfrastructure(builder.Configuration);
builder.Services.AddInstitucionalInfrastructure();
builder.Services.AddDocenteInfrastructure();
builder.Services.AddAttendanceInfrastructure();
builder.Services.AddEstudianteInfrastructure();

// Infraestructura de Estado Efímero y Tiempo Real
builder.Services.AddRedisInfrastructure(builder.Configuration);

// Motor Planificador en Segundo Plano (Quartz.NET - SRS 4.5)
builder.Services.AddQuartzBackgroundScheduler();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets();

// SignalR Attendance Projection Hub (Protegido con [Authorize])
app.MapHub<AttendanceHub>("/hubs/attendance");

// Flujo Estudiante SSR Estático Puro (Pasos 31, 32 y 33)
app.MapAttendanceStudentEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
