namespace SLAC.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SLAC.Core.Attendance.Services;
using SLAC.Core.Configuration;
using SLAC.Core.Session;
using SLAC.Core.Session.Models;
using SLAC.Infrastructure.Redis;
using Xunit;

public class RedisClaimConcurrencyTests
{
    private readonly ISessionCacheRepository _repo;

    public RedisClaimConcurrencyTests()
    {
        _repo = new FakeSessionCacheRepository();
    }

    private static ISessionCacheRepository CreateInMemoryRedisRepository()
    {
        var options = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost:6379,abortConnect=false"
        });
        var provider = new RedisConnectionProvider(options, NullLogger<RedisConnectionProvider>.Instance);
        return new RedisSessionCacheRepository(provider, NullLogger<RedisSessionCacheRepository>.Instance);
    }

    private static async Task<Guid> SetupActiveSessionOnRepoAsync(
        ISessionCacheRepository repo, string presenceCode = "456", int ttlMinutes = 20)
    {
        var sesionId = Guid.NewGuid();
        var state = new SessionEphemeralState
        {
            SesionId = sesionId,
            InstitucionId = Guid.NewGuid(),
            MateriaId = Guid.NewGuid(),
            DocenteId = Guid.NewGuid(),
            InicioVigencia = DateTimeOffset.UtcNow,
            Vencimiento = DateTimeOffset.UtcNow.AddMinutes(ttlMinutes),
            RotacionIndex = 0,
            TokenActual = "token-test",
            ContadorAsistentes = 0,
            EstaAbierta = true,
            CodigoPresenciaActual = presenceCode
        };

        await repo.SetActiveSessionAsync(state, TimeSpan.FromMinutes(ttlMinutes));
        return sesionId;
    }

    private Task<Guid> SetupActiveSessionAsync(string presenceCode = "456", int ttlMinutes = 20)
        => SetupActiveSessionOnRepoAsync(_repo, presenceCode, ttlMinutes);

    [Fact]
    public async Task TryClaimAttendance_ValidFirstTimeStudent_ShouldSucceedAndIncrementCounter()
    {
        var sesionId = await SetupActiveSessionAsync("456");
        var estudianteId = Guid.NewGuid();

        var result = await _repo.TryClaimAttendanceAsync(sesionId, estudianteId, "dev-1", "456");

        Assert.Equal(AttendanceClaimStatus.Success, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(1L, result.LiveAttendanceCount);
    }

    [Fact]
    public async Task TryClaimAttendance_DuplicateSameStudent_ShouldReturnAlreadyRegisteredWithoutIncrementingCounter()
    {
        var sesionId = await SetupActiveSessionAsync("456");
        var estudianteId = Guid.NewGuid();

        var first = await _repo.TryClaimAttendanceAsync(sesionId, estudianteId, "dev-1", "456");
        var second = await _repo.TryClaimAttendanceAsync(sesionId, estudianteId, "dev-1", "456");

        Assert.Equal(AttendanceClaimStatus.Success, first.Status);
        Assert.Equal(1L, first.LiveAttendanceCount);

        Assert.Equal(AttendanceClaimStatus.AlreadyRegistered, second.Status);
        Assert.True(second.IsAlreadyRegistered);
        Assert.Equal(1L, second.LiveAttendanceCount);
    }

    [Fact]
    public async Task TryClaimAttendance_RN05_DeviceConflict_ShouldRejectSecondStudent()
    {
        var sesionId = await SetupActiveSessionAsync("456");
        var estudiante1 = Guid.NewGuid();
        var estudiante2 = Guid.NewGuid();
        var sharedDevice = "hardware-uuid-99";

        var res1 = await _repo.TryClaimAttendanceAsync(sesionId, estudiante1, sharedDevice, "456");
        var res2 = await _repo.TryClaimAttendanceAsync(sesionId, estudiante2, sharedDevice, "456");

        Assert.Equal(AttendanceClaimStatus.Success, res1.Status);
        Assert.Equal(1L, res1.LiveAttendanceCount);

        Assert.Equal(AttendanceClaimStatus.DeviceConflict, res2.Status);
        Assert.True(res2.IsRejected);
    }

    [Fact]
    public async Task TryClaimAttendance_InvalidPresenceCode_ShouldReject()
    {
        var sesionId = await SetupActiveSessionAsync("789");
        var estudianteId = Guid.NewGuid();

        var result = await _repo.TryClaimAttendanceAsync(sesionId, estudianteId, "dev-1", "111");

        Assert.Equal(AttendanceClaimStatus.InvalidPresenceCode, result.Status);
        Assert.True(result.IsRejected);
    }

    [Fact]
    public async Task TryClaimAttendance_SessionInactive_ShouldReject()
    {
        var nonExistentSessionId = Guid.NewGuid();
        var estudianteId = Guid.NewGuid();

        var result = await _repo.TryClaimAttendanceAsync(nonExistentSessionId, estudianteId, "dev-1", "123");

        Assert.Equal(AttendanceClaimStatus.SessionInactive, result.Status);
        Assert.True(result.IsRejected);
    }

    [Fact]
    public async Task RollbackAttendanceClaim_AfterSuccess_ShouldDecrementCounterAndFreeDevice()
    {
        var sesionId = await SetupActiveSessionAsync("456");
        var estudiante1 = Guid.NewGuid();
        var estudiante2 = Guid.NewGuid();
        var device = "dev-alpha";

        var claim1 = await _repo.TryClaimAttendanceAsync(sesionId, estudiante1, device, "456");
        Assert.Equal(AttendanceClaimStatus.Success, claim1.Status);
        Assert.Equal(1L, claim1.LiveAttendanceCount);

        // Compensación transaccional
        await _repo.RollbackAttendanceClaimAsync(sesionId, estudiante1, device);

        // Estudiante 2 ahora puede usar el dispositivo previamente liberado
        var claim2 = await _repo.TryClaimAttendanceAsync(sesionId, estudiante2, device, "456");
        Assert.Equal(AttendanceClaimStatus.Success, claim2.Status);
        Assert.Equal(1L, claim2.LiveAttendanceCount);
    }

    [Fact]
    public async Task RollbackAttendanceClaim_WhenNotClaimed_ShouldNotDecrementCounter()
    {
        var sesionId = await SetupActiveSessionAsync("456");
        var estudiante1 = Guid.NewGuid();
        var estudiante2 = Guid.NewGuid();

        var claim1 = await _repo.TryClaimAttendanceAsync(sesionId, estudiante1, "dev-1", "456");
        Assert.Equal(1L, claim1.LiveAttendanceCount);

        // Rollback sobre un estudiante que nunca reclamó
        await _repo.RollbackAttendanceClaimAsync(sesionId, estudiante2, "dev-2");

        // Intentar registrar estudiante 1 de nuevo -> sigue existiendo
        var duplicate = await _repo.TryClaimAttendanceAsync(sesionId, estudiante1, "dev-1", "456");
        Assert.Equal(AttendanceClaimStatus.AlreadyRegistered, duplicate.Status);
        Assert.Equal(1L, duplicate.LiveAttendanceCount);
    }

    [Fact]
    public async Task RollbackAttendanceClaim_MismatchedDevice_ShouldNotDeleteOtherStudentDeviceClaim()
    {
        var sesionId = await SetupActiveSessionAsync("456");
        var estudiante1 = Guid.NewGuid();
        var estudiante2 = Guid.NewGuid();
        var estudiante3 = Guid.NewGuid();
        var deviceA = "device-hardware-mac-alpha";
        var deviceB = "device-hardware-mac-beta";

        // 1. Estudiante 1 reclama con Dispositivo A
        var claim1 = await _repo.TryClaimAttendanceAsync(sesionId, estudiante1, deviceA, "456");
        Assert.Equal(AttendanceClaimStatus.Success, claim1.Status);
        Assert.Equal(1L, claim1.LiveAttendanceCount);

        // 2. Estudiante 2 reclama con Dispositivo B
        var claim2 = await _repo.TryClaimAttendanceAsync(sesionId, estudiante2, deviceB, "456");
        Assert.Equal(AttendanceClaimStatus.Success, claim2.Status);
        Assert.Equal(2L, claim2.LiveAttendanceCount);

        // 3. Estudiante 2 sufre falla y se ejecuta rollback pasando por error o malicia Dispositivo A
        await _repo.RollbackAttendanceClaimAsync(sesionId, estudiante2, deviceA);

        // 4. Estudiante 2 ya no debe estar registrado, contador baja a 1
        var session = await _repo.GetActiveSessionAsync(sesionId);
        Assert.NotNull(session);
        Assert.Equal(1, session.ContadorAsistentes);

        // 5. El Dispositivo A NO debió ser liberado: Estudiante 3 intenta registrarse con Dispositivo A -> Debe ser rechazado (RN-05)
        var claim3 = await _repo.TryClaimAttendanceAsync(sesionId, estudiante3, deviceA, "456");
        Assert.Equal(AttendanceClaimStatus.DeviceConflict, claim3.Status);

        // 6. Si Estudiante 1 hace rollback con su propio Dispositivo A -> Se libera correctamente
        await _repo.RollbackAttendanceClaimAsync(sesionId, estudiante1, deviceA);

        // 7. Ahora Estudiante 3 sí puede registrarse con Dispositivo A
        var claim3Retry = await _repo.TryClaimAttendanceAsync(sesionId, estudiante3, deviceA, "456");
        Assert.Equal(AttendanceClaimStatus.Success, claim3Retry.Status);
        Assert.Equal(1L, claim3Retry.LiveAttendanceCount);
    }

    [Fact]
    public async Task Stress_100Students_SimultaneousClaims_ZeroCollisions()
    {
        await Assert100SimultaneousClaimsZeroCollisionsAsync(_repo);
    }

    [Fact]
    public async Task Stress_100UniqueStudents_ConcurrentClaims_ExactCounter100()
    {
        // Backward-compatible alias for the same test requirement
        await Assert100SimultaneousClaimsZeroCollisionsAsync(_repo);
    }

    [Fact]
    public async Task Stress_100Students_RedisFallback_SimultaneousClaims_ZeroCollisions()
    {
        var redisRepo = CreateInMemoryRedisRepository();
        await Assert100SimultaneousClaimsZeroCollisionsAsync(redisRepo);
    }

    private static async Task Assert100SimultaneousClaimsZeroCollisionsAsync(ISessionCacheRepository repo)
    {
        var sesionId = await SetupActiveSessionOnRepoAsync(repo, "456");
        const int studentCount = 100;
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, studentCount).Select(i => Task.Run(async () =>
        {
            var estudianteId = Guid.NewGuid();
            var deviceHash = $"device-stress-{i:D3}";
            await startGate.Task;
            return await repo.TryClaimAttendanceAsync(sesionId, estudianteId, deviceHash, "456");
        })).ToArray();

        startGate.SetResult();

        var results = await Task.WhenAll(tasks);

        // Todos deben haber sido absorbidos exitosamente
        Assert.Equal(studentCount, results.Count(r => r.IsSuccess));
        Assert.All(results, r => Assert.Equal(AttendanceClaimStatus.Success, r.Status));

        // Verificación de incremento monótono atómico estricto: 100 números distintos de 1 a 100
        var observedCounts = results.Select(r => r.LiveAttendanceCount).OrderBy(c => c).ToArray();
        Assert.Equal(studentCount, observedCounts.Distinct().Count());
        Assert.Equal(1L, observedCounts.First());
        Assert.Equal((long)studentCount, observedCounts.Last());

        // El estado final de la sesión o del repo debe reflejar exactamente 100
        var session = await repo.GetActiveSessionAsync(sesionId);
        Assert.NotNull(session);
        Assert.Equal(studentCount, session.ContadorAsistentes);
    }

    [Fact]
    public async Task Stress_SameStudent_50ConcurrentRequests_OnlyOneSuccess49Duplicates()
    {
        await AssertSameStudentDeduplicationAsync(_repo);
    }

    [Fact]
    public async Task Stress_SameStudent_RedisFallback_50ConcurrentRequests_OnlyOneSuccess49Duplicates()
    {
        var redisRepo = CreateInMemoryRedisRepository();
        await AssertSameStudentDeduplicationAsync(redisRepo);
    }

    private static async Task AssertSameStudentDeduplicationAsync(ISessionCacheRepository repo)
    {
        var sesionId = await SetupActiveSessionOnRepoAsync(repo, "456");
        var sameStudentId = Guid.NewGuid();
        const int concurrentRequests = 50;
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, concurrentRequests).Select(i => Task.Run(async () =>
        {
            await startGate.Task;
            return await repo.TryClaimAttendanceAsync(sesionId, sameStudentId, $"dev-{i:D2}", "456");
        })).ToArray();

        startGate.SetResult();

        var results = await Task.WhenAll(tasks);

        int successes = results.Count(r => r.Status == AttendanceClaimStatus.Success);
        int alreadyRegistered = results.Count(r => r.Status == AttendanceClaimStatus.AlreadyRegistered);
        int others = results.Count(r => r.Status != AttendanceClaimStatus.Success && r.Status != AttendanceClaimStatus.AlreadyRegistered);

        Assert.Equal(0, others);
        Assert.Equal(1, successes);
        Assert.Equal(concurrentRequests - 1, alreadyRegistered);

        var session = await repo.GetActiveSessionAsync(sesionId);
        Assert.NotNull(session);
        Assert.Equal(1, session.ContadorAsistentes);
    }

    [Fact]
    public async Task Stress_SameDevice_50DifferentStudents_OnlyOneSuccess49Conflicts()
    {
        await AssertSameDeviceContentionAcrossIterationsAsync(_repo, iterations: 10, concurrentStudents: 50);
    }

    [Fact]
    public async Task Stress_SameDevice_RedisFallback_50DifferentStudents_OnlyOneSuccess49Conflicts()
    {
        var redisRepo = CreateInMemoryRedisRepository();
        await AssertSameDeviceContentionAcrossIterationsAsync(redisRepo, iterations: 10, concurrentStudents: 50);
    }

    private static async Task AssertSameDeviceContentionAcrossIterationsAsync(
        ISessionCacheRepository repo, int iterations, int concurrentStudents)
    {
        for (int iter = 0; iter < iterations; iter++)
        {
            var sesionId = await SetupActiveSessionOnRepoAsync(repo, "456");
            var sharedDevice = $"loaned-device-iter-{iter:D2}";
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var tasks = Enumerable.Range(0, concurrentStudents).Select(_ => Task.Run(async () =>
            {
                var studentId = Guid.NewGuid();
                await startGate.Task;
                return await repo.TryClaimAttendanceAsync(sesionId, studentId, sharedDevice, "456");
            })).ToArray();

            startGate.SetResult();

            var results = await Task.WhenAll(tasks);

            int successes = results.Count(r => r.Status == AttendanceClaimStatus.Success);
            int conflicts = results.Count(r => r.Status == AttendanceClaimStatus.DeviceConflict);
            int others = results.Count(r => r.Status != AttendanceClaimStatus.Success && r.Status != AttendanceClaimStatus.DeviceConflict);

            Assert.True(others == 0, $"Iteration {iter} produced unexpected claim statuses: {string.Join(", ", results.Where(r => r.Status != AttendanceClaimStatus.Success && r.Status != AttendanceClaimStatus.DeviceConflict).Select(r => r.Status))}");
            Assert.Equal(1, successes);
            Assert.Equal(concurrentStudents - 1, conflicts);

            var session = await repo.GetActiveSessionAsync(sesionId);
            Assert.NotNull(session);
            Assert.Equal(1, session.ContadorAsistentes);
        }
    }
}
