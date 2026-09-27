using Microsoft.Extensions.Logging.Abstractions;
using SLAC.Core.Institucional.Entities;
using SLAC.Infrastructure.Repositories;
using Xunit;

namespace SLAC.Tests;

public class InstitucionalRepositoryTests
{
    private readonly SupabaseEspacioRepository _espacioRepo;
    private readonly SupabasePeriodoRepository _periodoRepo;
    private readonly SupabaseDiaNoLectivoRepository _diaNoLectivoRepo;

    public InstitucionalRepositoryTests()
    {
        // En pruebas unitarias aisladas inicializamos cliente Supabase con mock / placeholder
        var dummyClient = new Supabase.Client("https://placeholder.supabase.co", "placeholder-key");

        _espacioRepo = new SupabaseEspacioRepository(dummyClient, NullLogger<SupabaseEspacioRepository>.Instance);
        _periodoRepo = new SupabasePeriodoRepository(dummyClient, NullLogger<SupabasePeriodoRepository>.Instance);
        _diaNoLectivoRepo = new SupabaseDiaNoLectivoRepository(dummyClient, NullLogger<SupabaseDiaNoLectivoRepository>.Instance);
    }

    [Fact]
    public async Task EspacioRepository_CreateAndGet_ShouldSucceed()
    {
        // Arrange
        var instId = Guid.NewGuid();
        var espacio = new Espacio
        {
            InstitucionId = instId,
            Nombre = "Aula Magna 101",
            Tipo = "aula",
            Capacidad = 120
        };

        // Act
        var created = await _espacioRepo.CreateAsync(espacio);
        var list = await _espacioRepo.GetByInstitucionAsync(instId);

        // Assert
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Contains(list, e => e.Nombre == "Aula Magna 101" && e.Capacidad == 120);
    }

    [Fact]
    public async Task PeriodoRepository_CreateAndRetrieve_ShouldSucceed()
    {
        // Arrange
        var instId = Guid.NewGuid();
        var periodo = new PeriodoAcademico
        {
            InstitucionId = instId,
            Nombre = "Semestre I-2026",
            FechaInicio = new DateOnly(2026, 2, 1),
            FechaFin = new DateOnly(2026, 6, 30)
        };

        // Act
        var created = await _periodoRepo.CreateAsync(periodo);
        var retrieved = await _periodoRepo.GetByIdAsync(created.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Semestre I-2026", retrieved.Nombre);
        Assert.Equal(new DateOnly(2026, 2, 1), retrieved.FechaInicio);
    }

    [Fact]
    public async Task DiaNoLectivoRepository_CheckHoliday_ShouldDetectAccurately()
    {
        // Arrange
        var instId = Guid.NewGuid();
        var fechaFeriado = new DateOnly(2026, 5, 1);
        var diaNoLectivo = new DiaNoLectivo
        {
            InstitucionId = instId,
            Fecha = fechaFeriado,
            Motivo = "Día del Trabajo"
        };

        // Act
        await _diaNoLectivoRepo.CreateAsync(diaNoLectivo);
        var isHoliday = await _diaNoLectivoRepo.IsDiaNoLectivoAsync(instId, fechaFeriado);
        var isRegularDay = await _diaNoLectivoRepo.IsDiaNoLectivoAsync(instId, new DateOnly(2026, 5, 2));

        // Assert
        Assert.True(isHoliday);
        Assert.False(isRegularDay);
    }
}
