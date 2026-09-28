using Microsoft.Extensions.Logging.Abstractions;
using SLAC.Core.Docentes.Entities;
using SLAC.Infrastructure.Repositories;
using Xunit;

namespace SLAC.Tests;

public class DocenteMateriaTests
{
    [Fact]
    public void Materia_ConDiasLunesAViernes_EsValida()
    {
        // Arrange
        var materia = new Materia
        {
            Codigo = "MAT-101",
            Nombre = "Cálculo I",
            Grupo = "1",
            PeriodoId = Guid.NewGuid(),
            DocenteId = Guid.NewGuid(),
            EspacioId = Guid.NewGuid(),
            HoraInicio = new TimeSpan(8, 0, 0),
            Dias = "L,M,V"
        };

        // Act
        var (esValida, error) = materia.ValidarReglasNegocio();

        // Assert
        Assert.True(esValida);
        Assert.Null(error);
        Assert.Equal("Lunes, Martes, Viernes", materia.ObtenerDiasLegibles());
    }

    [Theory]
    [InlineData("S")] // Sábado
    [InlineData("D")] // Domingo
    [InlineData("L,M,S")] // Fin de semana mezclado
    [InlineData("XYZ")] // Sigla inválida
    public void Materia_ConDiasFinDeSemanaOInvalidos_ViolaRN01(string diasInvalidos)
    {
        // Arrange
        var materia = new Materia
        {
            Codigo = "INF-201",
            Nombre = "Estructuras de Datos",
            Grupo = "A",
            PeriodoId = Guid.NewGuid(),
            DocenteId = Guid.NewGuid(),
            EspacioId = Guid.NewGuid(),
            HoraInicio = new TimeSpan(10, 30, 0),
            Dias = diasInvalidos
        };

        // Act
        var (esValida, error) = materia.ValidarReglasNegocio();

        // Assert
        Assert.False(esValida);
        Assert.NotNull(error);
        Assert.Contains("RN-01", error);
    }

    [Fact]
    public void Materia_SinDias_RetornaError()
    {
        // Arrange
        var materia = new Materia
        {
            Codigo = "FIS-100",
            Nombre = "Física Básica",
            Grupo = "1",
            PeriodoId = Guid.NewGuid(),
            DocenteId = Guid.NewGuid(),
            EspacioId = Guid.NewGuid(),
            HoraInicio = new TimeSpan(14, 0, 0),
            Dias = ""
        };

        // Act
        var (esValida, error) = materia.ValidarReglasNegocio();

        // Assert
        Assert.False(esValida);
        Assert.Contains("al menos un día", error);
    }

    [Fact]
    public async Task RepositoriosDocenteYMateria_GuardarYListar_FuncionanCorrectamente()
    {
        // Arrange
        var fakeClient = new Supabase.Client("https://fake.supabase.co", "fake-key");
        var docenteRepo = new SupabaseDocenteRepository(fakeClient, NullLogger<SupabaseDocenteRepository>.Instance);
        var materiaRepo = new SupabaseMateriaRepository(fakeClient, NullLogger<SupabaseMateriaRepository>.Instance);

        var institucionId = Guid.NewGuid();
        var docente = new Docente
        {
            InstitucionId = institucionId,
            UserId = Guid.NewGuid(),
            Nombres = "María",
            Apellidos = "Gonzales",
            Correo = "mgonzales@universidad.edu"
        };

        // Act: Guardar docente
        var docenteGuardado = await docenteRepo.GuardarAsync(docente);
        var docenteObtenido = await docenteRepo.ObtenerPorIdAsync(docenteGuardado.Id);

        Assert.NotNull(docenteObtenido);
        Assert.Equal("María Gonzales", docenteObtenido.NombreCompleto);

        // Act: Guardar materias
        var materia1 = new Materia
        {
            InstitucionId = institucionId,
            DocenteId = docenteGuardado.Id,
            PeriodoId = Guid.NewGuid(),
            EspacioId = Guid.NewGuid(),
            Codigo = "SIS-301",
            Nombre = "Base de Datos I",
            Grupo = "1",
            HoraInicio = new TimeSpan(8, 0, 0),
            Dias = "L,M,X",
            Estado = "activa"
        };

        var materia2 = new Materia
        {
            InstitucionId = institucionId,
            DocenteId = docenteGuardado.Id,
            PeriodoId = Guid.NewGuid(),
            EspacioId = Guid.NewGuid(),
            Codigo = "SIS-302",
            Nombre = "Sistemas Operativos",
            Grupo = "2",
            HoraInicio = new TimeSpan(10, 0, 0),
            Dias = "J,V",
            Estado = "activa"
        };

        await materiaRepo.GuardarAsync(materia1);
        await materiaRepo.GuardarAsync(materia2);

        // Assert: Listar por docente
        var materiasDocente = await materiaRepo.ListarPorDocenteAsync(docenteGuardado.Id);
        Assert.Equal(2, materiasDocente.Count);

        // Assert: Listar activas por día (Lunes -> debe devolver solo materia 1)
        var materiasLunes = await materiaRepo.ListarActivasPorDiaAsync(institucionId, "L");
        Assert.Single(materiasLunes);
        Assert.Equal("SIS-301", materiasLunes[0].Codigo);

        // Assert: Listar activas por día (Viernes -> debe devolver solo materia 2)
        var materiasViernes = await materiaRepo.ListarActivasPorDiaAsync(institucionId, "V");
        Assert.Single(materiasViernes);
        Assert.Equal("SIS-302", materiasViernes[0].Codigo);

        // Act: Archivar materia 1
        var archivada = await materiaRepo.ArchivarAsync(materia1.Id);
        Assert.True(archivada);

        var materiasLunesPostArchivo = await materiaRepo.ListarActivasPorDiaAsync(institucionId, "L");
        Assert.Empty(materiasLunesPostArchivo);
    }
}
