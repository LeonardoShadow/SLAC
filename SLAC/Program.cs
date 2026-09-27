using MudBlazor.Services;
// Core y Módulos de SLAC
using SLAC.Components;
using SLAC.Core.Security;
using SLAC.Features.Attendance.Background;
using SLAC.Features.Attendance.Hubs;
using SLAC.Infrastructure.Data;
using SLAC.Infrastructure.Redis;
using SLAC.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor UI Services
builder.Services.AddMudServices();

// Core Security & Criptografía (ES256)
builder.Services.AddSingleton<IKeyManager, KeyManager>();
builder.Services.AddSingleton<ITokenService, TokenService>();

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

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();

// SignalR Attendance Projection Hub
app.MapHub<AttendanceHub>("/hubs/attendance");

// Flujo Estudiante SSR Estático Puro (Pasos 31, 32 y 33)
app.MapAttendanceStudentEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
