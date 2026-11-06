namespace SLAC.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SLAC.Core.Configuration;
using SLAC.Core.Session;
using SLAC.Infrastructure.Redis;
using Xunit;

/// <summary>
/// Empirical challenge harness stress-testing Projection Lock contention,
/// forced takeover semantics, and lease invalidation under high concurrency.
/// Tested against both FakeSessionCacheRepository and RedisSessionCacheRepository.
/// </summary>
public class ProjectionLockContentionEmpiricalTests
{
    private static ISessionCacheRepository CreateFakeRepo() => new FakeSessionCacheRepository();

    private static ISessionCacheRepository CreateRedisFallbackRepo()
    {
        var options = Options.Create(new RedisOptions
        {
            ConnectionString = "localhost:6379,abortConnect=false"
        });
        var connProvider = new RedisConnectionProvider(options, NullLogger<RedisConnectionProvider>.Instance);
        return new RedisSessionCacheRepository(connProvider, NullLogger<RedisSessionCacheRepository>.Instance);
    }

    public static IEnumerable<object[]> GetRepositoryInstances()
    {
        yield return new object[] { "FakeSessionCacheRepository", CreateFakeRepo() };
        yield return new object[] { "RedisSessionCacheRepository", CreateRedisFallbackRepo() };
    }

    [Theory]
    [MemberData(nameof(GetRepositoryInstances))]
    public async Task Contention_10ConcurrentWorkers_ExactlyOneWins(string repoName, ISessionCacheRepository repo)
    {
        _ = repoName;
        var sesionId = Guid.NewGuid();
        var ttl = TimeSpan.FromSeconds(30);
        const int concurrentWorkers = 10;

        // Barrier to release all 10 workers at the exact same instant
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, concurrentWorkers).Select(i => Task.Run(async () =>
        {
            await tcs.Task; // Wait for concurrent signal
            var leaseId = $"worker-lease-{i}-{Guid.NewGuid():N}";
            var acquired = await repo.TryAcquireProjectionLockAsync(sesionId, leaseId, ttl, force: false);
            return (LeaseId: leaseId, Acquired: acquired);
        })).ToArray();

        // Release all workers simultaneously
        tcs.SetResult(true);

        var results = await Task.WhenAll(tasks);

        var winners = results.Where(r => r.Acquired).ToList();
        var losers = results.Where(r => !r.Acquired).ToList();

        // Invariant 1: Exactly 1 lease acquires ownership without force: true
        Assert.Single(winners);
        Assert.Equal(concurrentWorkers - 1, losers.Count);

        var winningLease = winners[0].LeaseId;

        // Invariant 2: When an owner holds the lock, a second lease with force: false is rejected
        var intruderLease = $"intruder-{Guid.NewGuid():N}";
        var intruderAttempt = await repo.TryAcquireProjectionLockAsync(sesionId, intruderLease, ttl, force: false);
        Assert.False(intruderAttempt);

        // Invariant 2b: Winner can refresh its own lease successfully
        var winnerRefresh = await repo.RefreshProjectionLockAsync(sesionId, winningLease, ttl);
        Assert.True(winnerRefresh);

        // Invariant 3: When a second lease calls force: true, it takes over ownership immediately
        var takeoverLease = $"takeover-{Guid.NewGuid():N}";
        var takeoverResult = await repo.TryAcquireProjectionLockAsync(sesionId, takeoverLease, ttl, force: true);
        Assert.True(takeoverResult);

        // Invariant 4: When the old lease attempts to refresh, it is rejected because lease ID no longer matches
        var oldLeaseRefresh = await repo.RefreshProjectionLockAsync(sesionId, winningLease, ttl);
        Assert.False(oldLeaseRefresh);

        // Invariant 5: When old lease attempts to release, it must not destroy the new lease
        await repo.ReleaseProjectionLockAsync(sesionId, winningLease);

        // New leaseholder must still retain the lock and be able to refresh
        var newLeaseRefresh = await repo.RefreshProjectionLockAsync(sesionId, takeoverLease, ttl);
        Assert.True(newLeaseRefresh);

        // Third party with force: false must still be rejected
        var thirdPartyAttempt = await repo.TryAcquireProjectionLockAsync(sesionId, $"thirdparty-{Guid.NewGuid():N}", ttl, force: false);
        Assert.False(thirdPartyAttempt);

        // Invariant 6: When new owner releases, lock is freed
        await repo.ReleaseProjectionLockAsync(sesionId, takeoverLease);

        // Now an arbitrary new lease can acquire without force
        var postReleaseLease = $"post-release-{Guid.NewGuid():N}";
        var postReleaseAcquired = await repo.TryAcquireProjectionLockAsync(sesionId, postReleaseLease, ttl, force: false);
        Assert.True(postReleaseAcquired);
    }

    [Theory]
    [MemberData(nameof(GetRepositoryInstances))]
    public async Task MassiveConcurrentContention_50Workers_NoCorruptState(string repoName, ISessionCacheRepository repo)
    {
        _ = repoName;
        var sesionId = Guid.NewGuid();
        var ttl = TimeSpan.FromSeconds(30);
        const int totalTasks = 50;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, totalTasks).Select(i => Task.Run(async () =>
        {
            await tcs.Task;
            var leaseId = $"stress-tab-{i}";
            return await repo.TryAcquireProjectionLockAsync(sesionId, leaseId, ttl, force: false);
        })).ToArray();

        tcs.SetResult(true);

        var results = await Task.WhenAll(tasks);

        // Under 50 simultaneous tabs with force: false, strictly 1 tab must acquire the lock
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(totalTasks - 1, results.Count(r => !r));
    }

    [Theory]
    [MemberData(nameof(GetRepositoryInstances))]
    public async Task RapidSequentialTakeovers_LastForceWins(string repoName, ISessionCacheRepository repo)
    {
        _ = repoName;
        var sesionId = Guid.NewGuid();
        var ttl = TimeSpan.FromSeconds(30);

        var lease1 = "lease-1";
        var lease2 = "lease-2";
        var lease3 = "lease-3";

        // Lease 1 acquires
        Assert.True(await repo.TryAcquireProjectionLockAsync(sesionId, lease1, ttl, force: false));

        // Lease 2 forces takeover
        Assert.True(await repo.TryAcquireProjectionLockAsync(sesionId, lease2, ttl, force: true));
        Assert.False(await repo.RefreshProjectionLockAsync(sesionId, lease1, ttl));
        Assert.True(await repo.RefreshProjectionLockAsync(sesionId, lease2, ttl));

        // Lease 3 forces takeover
        Assert.True(await repo.TryAcquireProjectionLockAsync(sesionId, lease3, ttl, force: true));
        Assert.False(await repo.RefreshProjectionLockAsync(sesionId, lease1, ttl));
        Assert.False(await repo.RefreshProjectionLockAsync(sesionId, lease2, ttl));
        Assert.True(await repo.RefreshProjectionLockAsync(sesionId, lease3, ttl));

        // Cleanup
        await repo.ReleaseProjectionLockAsync(sesionId, lease3);
    }
}
