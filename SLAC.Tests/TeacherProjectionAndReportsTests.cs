using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using SLAC.Core.Attendance.Entities;
using SLAC.Core.Attendance.Repositories;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Docentes.Entities;
using SLAC.Core.Docentes.Repositories;
using SLAC.Core.Estudiantes.Entities;
using SLAC.Core.Estudiantes.Repositories;
using SLAC.Core.Security;
using SLAC.Features.Attendance.Services;

namespace SLAC.Tests;

public class TeacherProjectionAndReportsTests
{
    private readonly Guid _institucionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _materiaId = Guid.NewGuid();

    [Fact]
    public void QrCodeGeneratorService_GeneratesValidSvg()
    {
        // Arrange
        var service = new QrCodeGeneratorService();
        const string payload = "https://slac.edu/a/11111111-1111-1111-1111-111111111111?t=mock-token";

        // Act
        var svg = service.GenerateSvg(payload, pixelsPerModule: 10);

        // Assert
        Assert.NotNull(svg);
        Assert.NotEmpty(svg);
        Assert.Contains("<svg", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</svg>", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("viewBox", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QrCodeGeneratorService_EmptyInput_ReturnsEmptyString()
    {
        // Arrange
        var service = new QrCodeGeneratorService();

        // Act & Assert
        Assert.Equal(string.Empty, service.GenerateSvg(string.Empty));
        Assert.Equal(string.Empty, service.GenerateSvg("   "));
    }

    [Fact]
    public async Task ReporteAsistenciaService_ObtenerReporteFaltas_ReturnsOnlyFaltas()
    {
        // Arrange
        var listaRepo = new MockListaRepo();
        var detalleRepo = new MockDetalleRepo();
        var estudianteRepo = new MockEstudianteRepo();
        var materiaRepo = new MockMateriaRepo();

        var materia = new Materia
        {
            Id = _materiaId,
            InstitucionId = _institucionId,
            Nombre = "Cálculo I",
            Codigo = "MAT-101",
            HoraInicio = new TimeSpan(8, 0, 0),
            Dias = "L,M,V"
        };
        await materiaRepo.GuardarAsync(materia);

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var sesion = new ListaAsistencia
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            MateriaId = _materiaId,
            Fecha = hoy,
            HoraInicio = new TimeSpan(8, 0, 0),
            Estado = "Cerrada"
        };
        await listaRepo.CrearOActualizarAsync(sesion);

        // Estudiante 1: Presente
        var est1 = new Estudiante
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            Codigo = "EST-001",
            Nombres = "Carlos",
            Apellidos = "Vargas",
            Correo = "carlos@universidad.edu"
        };
        await estudianteRepo.CrearOActualizarAsync(est1);
        await detalleRepo.RegistrarAsistenciaAsync(new AsistenciaDetalle
        {
            Id = Guid.NewGuid(),
            ListaId = sesion.Id,
            EstudianteId = est1.Id,
            Estado = "Presente"
        });

        // Estudiante 2: Falta (Ausente)
        var est2 = new Estudiante
        {
            Id = Guid.NewGuid(),
            InstitucionId = _institucionId,
            Codigo = "EST-002",
            Nombres = "María",
            Apellidos = "Rojas",
            Correo = "maria@universidad.edu"
        };
        await estudianteRepo.CrearOActualizarAsync(est2);
        await detalleRepo.RegistrarAsistenciaAsync(new AsistenciaDetalle
        {
            Id = Guid.NewGuid(),
            ListaId = sesion.Id,
            EstudianteId = est2.Id,
            Estado = "Falta",
            Origen = "Ausente"
        });

        var service = new ReporteAsistenciaService(listaRepo, detalleRepo, estudianteRepo, materiaRepo, NullLogger<ReporteAsistenciaService>.Instance);

        // Act
        var faltas = await service.ObtenerReporteFaltasAsync(_materiaId);

        // Assert
        Assert.Single(faltas);
        var falta = faltas[0];
        Assert.Equal("EST-002", falta.CodigoEstudiante);
        Assert.Equal("María Rojas", falta.NombreEstudiante);
        Assert.Equal("Falta", falta.Estado);
        Assert.Equal("Ausente", falta.Origen);
        Assert.Equal("Cálculo I", falta.NombreMateria);
    }

    [Fact]
    public async Task ReporteAsistenciaService_GenerarExcelFaltas_ProducesValidClosedXmlWorkbook()
    {
        // Arrange
        var items = new List<ItemReporteFalta>
        {
            new(
                DetalleId: Guid.NewGuid(),
                ListaId: Guid.NewGuid(),
                EstudianteId: Guid.NewGuid(),
                CodigoEstudiante: "100234",
                NombreEstudiante: "Ana García",
                CorreoEstudiante: "ana.garcia@universidad.edu",
                MateriaId: _materiaId,
                NombreMateria: "Física Básica",
                CodigoMateria: "FIS-100",
                Fecha: new DateOnly(2026, 9, 27),
                HoraInicio: new TimeSpan(10, 0, 0),
                Estado: "Falta",
                Origen: "Suscripción tardía",
                CreadoEn: DateTimeOffset.UtcNow
            )
        };

        var service = new ReporteAsistenciaService(
            new MockListaRepo(),
            new MockDetalleRepo(),
            new MockEstudianteRepo(),
            new MockMateriaRepo(),
            NullLogger<ReporteAsistenciaService>.Instance);

        // Act
        var excelBytes = await service.GenerarExcelFaltasAsync(items, "Física Básica", "FIS-100");

        // Assert
        Assert.NotNull(excelBytes);
        Assert.NotEmpty(excelBytes);

        // Abrir con ClosedXML para verificar la estructura
        await using var stream = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();

        Assert.NotNull(worksheet);
        Assert.Equal("Faltas", worksheet.Name);

        // Verificar encabezados y datos
        Assert.Contains("SLAC", worksheet.Cell(1, 1).GetString());
        Assert.Contains("Física Básica", worksheet.Cell(2, 1).GetString());
        Assert.Equal("Código Estudiante", worksheet.Cell(5, 2).GetString());
        Assert.Equal("100234", worksheet.Cell(6, 2).GetString());
        Assert.Equal("Ana García", worksheet.Cell(6, 3).GetString());
        Assert.Equal("Falta", worksheet.Cell(6, 7).GetString());
        Assert.Equal("Suscripción tardía", worksheet.Cell(6, 8).GetString());
    }

    [Fact]
    public async Task ReporteAsistenciaService_GenerarCsvFaltas_ProducesUtf8BomAndValidContent()
    {
        // Arrange
        var items = new List<ItemReporteFalta>
        {
            new(
                DetalleId: Guid.NewGuid(),
                ListaId: Guid.NewGuid(),
                EstudianteId: Guid.NewGuid(),
                CodigoEstudiante: "200987",
                NombreEstudiante: "Roberto Nuñez",
                CorreoEstudiante: "roberto@universidad.edu",
                MateriaId: _materiaId,
                NombreMateria: "Álgebra Lineal",
                CodigoMateria: "MAT-102",
                Fecha: new DateOnly(2026, 9, 27),
                HoraInicio: new TimeSpan(14, 0, 0),
                Estado: "Falta",
                Origen: "Ausente",
                CreadoEn: DateTimeOffset.UtcNow
            )
        };

        var service = new ReporteAsistenciaService(
            new MockListaRepo(),
            new MockDetalleRepo(),
            new MockEstudianteRepo(),
            new MockMateriaRepo(),
            NullLogger<ReporteAsistenciaService>.Instance);

        // Act
        var csvBytes = await service.GenerarCsvFaltasAsync(items);

        // Assert
        Assert.NotNull(csvBytes);
        Assert.True(csvBytes.Length >= 3);

        // Verificar UTF-8 BOM: 0xEF, 0xBB, 0xBF
        Assert.Equal(0xEF, csvBytes[0]);
        Assert.Equal(0xBB, csvBytes[1]);
        Assert.Equal(0xBF, csvBytes[2]);

        var csvText = Encoding.UTF8.GetString(csvBytes);
        Assert.Contains("Nro,Codigo,Estudiante,Correo", csvText);
        Assert.Contains("200987", csvText);
        Assert.Contains("Roberto Nuñez", csvText);
        Assert.Contains("Ausente", csvText);
    }

    [Fact]
    public void QrRotation_GeneratesDistinctValidTokensAcrossRotations()
    {
        // Arrange
        var keyManager = new KeyManager();
        var tokenService = new TokenService(keyManager);
        var sesionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Act
        // Rotación 0 a los 0 segundos
        var token0 = tokenService.GenerateSessionQrToken(sesionId, _institucionId, now, 20, rotacionIndex: 0);
        // Rotación 1 a los 15 segundos
        var token1 = tokenService.GenerateSessionQrToken(sesionId, _institucionId, now.AddSeconds(-15), 20, rotacionIndex: 1);
        // Rotación 2 a los 30 segundos
        var token2 = tokenService.GenerateSessionQrToken(sesionId, _institucionId, now.AddSeconds(-30), 20, rotacionIndex: 2);

        // Assert
        Assert.NotEqual(token0, token1);
        Assert.NotEqual(token1, token2);

        // Todos deben validar correctamente en su respectiva ventana temporal
        Assert.True(tokenService.TryValidateSessionQrToken(token0, out var t0, out _));
        Assert.True(tokenService.TryValidateSessionQrToken(token1, out var t1, out _));
        Assert.True(tokenService.TryValidateSessionQrToken(token2, out var t2, out _));

        Assert.Equal(0, t0!.RotacionIndex);
        Assert.Equal(1, t1!.RotacionIndex);
        Assert.Equal(2, t2!.RotacionIndex);
    }

    // --- Repositorios Mock para Aislamiento de Pruebas ---
    private class MockListaRepo : IListaAsistenciaRepository
    {
        private readonly List<ListaAsistencia> _listas = [];

        public Task<ListaAsistencia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_listas.FirstOrDefault(l => l.Id == id));

        public Task<ListaAsistencia?> ObtenerPorMateriaYFechaAsync(Guid materiaId, DateOnly fecha, CancellationToken ct = default) =>
            Task.FromResult(_listas.FirstOrDefault(l => l.MateriaId == materiaId && l.Fecha == fecha));

        public Task<IReadOnlyList<ListaAsistencia>> ListarPorMateriaAsync(Guid materiaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ListaAsistencia>>([.. _listas.Where(l => l.MateriaId == materiaId)]);

        public Task<ListaAsistencia> CrearOActualizarAsync(ListaAsistencia lista, CancellationToken ct = default)
        {
            if (lista.Id == Guid.Empty) lista.Id = Guid.NewGuid();
            _listas.RemoveAll(l => l.Id == lista.Id);
            _listas.Add(lista);
            return Task.FromResult(lista);
        }

        public Task ActualizarEstadoAsync(Guid id, string nuevoEstado, TimeSpan? horaCierre = null, CancellationToken ct = default)
        {
            var l = _listas.FirstOrDefault(x => x.Id == id);
            if (l != null)
            {
                l.Estado = nuevoEstado;
                l.HoraCierre = horaCierre;
            }
            return Task.CompletedTask;
        }

        public Task ActualizarTotalesAsync(Guid id, int totalSuscritos, int totalPresentes, int totalFaltas, CancellationToken ct = default)
        {
            var l = _listas.FirstOrDefault(x => x.Id == id);
            if (l != null)
            {
                l.TotalSuscritos = totalSuscritos;
                l.TotalPresentes = totalPresentes;
                l.TotalFaltas = totalFaltas;
            }
            return Task.CompletedTask;
        }
    }

    private class MockDetalleRepo : IAsistenciaDetalleRepository
    {
        private readonly List<AsistenciaDetalle> _detalles = [];

        public Task<IReadOnlyList<AsistenciaDetalle>> ListarPorListaAsync(Guid listaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AsistenciaDetalle>>([.. _detalles.Where(d => d.ListaId == listaId)]);

        public Task<AsistenciaDetalle?> ObtenerPorListaYEstudianteAsync(Guid listaId, Guid estudianteId, CancellationToken ct = default) =>
            Task.FromResult(_detalles.FirstOrDefault(d => d.ListaId == listaId && d.EstudianteId == estudianteId));

        public Task<AsistenciaDetalle> RegistrarAsistenciaAsync(AsistenciaDetalle detalle, CancellationToken ct = default)
        {
            if (detalle.Id == Guid.Empty) detalle.Id = Guid.NewGuid();
            _detalles.RemoveAll(d => d.ListaId == detalle.ListaId && d.EstudianteId == detalle.EstudianteId);
            _detalles.Add(detalle);
            return Task.FromResult(detalle);
        }

        public Task<int> RegistrarFaltasIdempotenteAsync(Guid listaId, Guid institucionId, IEnumerable<Guid> estudiantesIds, CancellationToken ct = default)
        {
            int added = 0;
            foreach (var estId in estudiantesIds)
            {
                if (!_detalles.Any(d => d.ListaId == listaId && d.EstudianteId == estId))
                {
                    _detalles.Add(new AsistenciaDetalle
                    {
                        Id = Guid.NewGuid(),
                        ListaId = listaId,
                        EstudianteId = estId,
                        Estado = "Falta",
                        Origen = "Ausente"
                    });
                    added++;
                }
            }
            return Task.FromResult(added);
        }
    }

    private class MockEstudianteRepo : IEstudianteRepository
    {
        private readonly List<Estudiante> _estudiantes = [];

        public Task<Estudiante?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_estudiantes.FirstOrDefault(e => e.Id == id));

        public Task<Estudiante?> ObtenerPorCodigoAsync(Guid institucionId, string codigo, CancellationToken ct = default) =>
            Task.FromResult(_estudiantes.FirstOrDefault(e => e.InstitucionId == institucionId && e.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase)));

        public Task<Estudiante?> ObtenerPorCorreoAsync(Guid institucionId, string correo, CancellationToken ct = default) =>
            Task.FromResult(_estudiantes.FirstOrDefault(e => e.InstitucionId == institucionId && e.Correo.Equals(correo, StringComparison.OrdinalIgnoreCase)));

        public Task<Estudiante> CrearOActualizarAsync(Estudiante estudiante, CancellationToken ct = default)
        {
            if (estudiante.Id == Guid.Empty) estudiante.Id = Guid.NewGuid();
            _estudiantes.RemoveAll(e => e.Id == estudiante.Id);
            _estudiantes.Add(estudiante);
            return Task.FromResult(estudiante);
        }

        public Task<IReadOnlyList<Estudiante>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Estudiante>>([.. _estudiantes.Where(e => e.InstitucionId == institucionId)]);
    }

    private class MockMateriaRepo : IMateriaRepository
    {
        private readonly List<Materia> _materias = [];

        public Task<IReadOnlyList<Materia>> ListarPorDocenteAsync(Guid docenteId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Materia>>([.. _materias.Where(m => m.DocenteId == docenteId)]);

        public Task<IReadOnlyList<Materia>> ListarPorInstitucionAsync(Guid institucionId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Materia>>([.. _materias.Where(m => m.InstitucionId == institucionId)]);

        public Task<IReadOnlyList<Materia>> ListarActivasPorDiaAsync(Guid institucionId, string diaSigla, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Materia>>([.. _materias.Where(m => m.InstitucionId == institucionId && m.Estado == "activa" && m.ObtenerListaDias().Contains(diaSigla))]);

        public Task<Materia?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_materias.FirstOrDefault(m => m.Id == id));

        public Task<Materia> GuardarAsync(Materia materia, CancellationToken ct = default)
        {
            if (materia.Id == Guid.Empty) materia.Id = Guid.NewGuid();
            _materias.RemoveAll(m => m.Id == materia.Id);
            _materias.Add(materia);
            return Task.FromResult(materia);
        }

        public Task<bool> ArchivarAsync(Guid id, CancellationToken ct = default)
        {
            var m = _materias.FirstOrDefault(x => x.Id == id);
            if (m != null)
            {
                m.Estado = "archivada";
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }
}
