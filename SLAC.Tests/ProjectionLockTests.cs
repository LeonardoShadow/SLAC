namespace SLAC.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using SLAC.Core.Session;
using Xunit;

public class ProjectionLockTests
{
    private readonly ISessionCacheRepository _repo;

    public ProjectionLockTests()
    {
        _repo = new FakeSessionCacheRepository();
    }

    [Fact]
    public async Task TryAcquireProjectionLock_WhenLockIsFree_ShouldAcquireSuccessfully()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var ttl = TimeSpan.FromSeconds(30);

        var acquired = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);

        Assert.True(acquired);
    }

    [Fact]
    public async Task TryAcquireProjectionLock_WhenHeldBySameLease_ShouldBeReentrantAndSucceed()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var ttl = TimeSpan.FromSeconds(30);

        var first = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);
        var second = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);

        Assert.True(first);
        Assert.True(second);
    }

    [Fact]
    public async Task TryAcquireProjectionLock_WhenHeldByDifferentLease_ShouldFailWithConflict()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var leaseB = "lease-B";
        var ttl = TimeSpan.FromSeconds(30);

        var first = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);
        var second = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseB, ttl);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task RefreshProjectionLock_WhenHeldByCaller_ShouldExtendLease()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var ttl = TimeSpan.FromSeconds(30);

        await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);
        var refreshed = await _repo.RefreshProjectionLockAsync(sesionId, leaseA, ttl);

        Assert.True(refreshed);
    }

    [Fact]
    public async Task RefreshProjectionLock_WhenCalledByDifferentLease_ShouldFail()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var leaseB = "lease-B";
        var ttl = TimeSpan.FromSeconds(30);

        await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);
        var refreshed = await _repo.RefreshProjectionLockAsync(sesionId, leaseB, ttl);

        Assert.False(refreshed);
    }

    [Fact]
    public async Task RefreshProjectionLock_WhenExpired_ShouldFail()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var ttl = TimeSpan.FromMilliseconds(50);

        await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);
        await Task.Delay(100);

        var refreshed = await _repo.RefreshProjectionLockAsync(sesionId, leaseA, TimeSpan.FromSeconds(30));

        Assert.False(refreshed);
    }

    [Fact]
    public async Task ReleaseProjectionLock_WhenHeldByCaller_ShouldReleaseAndAllowNewAcquisition()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var leaseB = "lease-B";
        var ttl = TimeSpan.FromSeconds(30);

        await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);
        await _repo.ReleaseProjectionLockAsync(sesionId, leaseA);

        var acquiredB = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseB, ttl);

        Assert.True(acquiredB);
    }

    [Fact]
    public async Task ReleaseProjectionLock_WhenCalledByUnauthorizedLease_ShouldNotReleaseLock()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var leaseB = "lease-B";
        var leaseC = "lease-C";
        var ttl = TimeSpan.FromSeconds(30);

        await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);
        await _repo.ReleaseProjectionLockAsync(sesionId, leaseB);

        var acquiredC = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseC, ttl);

        Assert.False(acquiredC);
    }

    [Fact]
    public async Task ConcurrentAcquisition_MultipleTabs_OnlyOneWins()
    {
        var sesionId = Guid.NewGuid();
        var ttl = TimeSpan.FromSeconds(30);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            return await _repo.TryAcquireProjectionLockAsync(sesionId, $"lease-{i}", ttl);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(19, results.Count(r => !r));
    }

    [Fact]
    public async Task InvalidateSession_ShouldClearProjectionLock()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var leaseB = "lease-B";
        var ttl = TimeSpan.FromSeconds(30);

        await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl);
        await _repo.InvalidateSessionAsync(sesionId);

        var acquiredB = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseB, ttl);

        Assert.True(acquiredB);
    }

    [Fact]
    public async Task TryAcquireProjectionLock_WhenHeldByDifferentLease_WithForceTrue_ShouldTakeOver()
    {
        var sesionId = Guid.NewGuid();
        var leaseA = "lease-A";
        var leaseB = "lease-B";
        var ttl = TimeSpan.FromSeconds(30);

        var acquiredA = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseA, ttl, force: false);
        Assert.True(acquiredA);

        // Attempting normal acquisition by B should fail
        var acquiredBWithoutForce = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseB, ttl, force: false);
        Assert.False(acquiredBWithoutForce);

        // Forced takeover by B should succeed and steal the lease
        var acquiredBWithForce = await _repo.TryAcquireProjectionLockAsync(sesionId, leaseB, ttl, force: true);
        Assert.True(acquiredBWithForce);

        // Now previous leaseholder A cannot refresh the lock
        var refreshedA = await _repo.RefreshProjectionLockAsync(sesionId, leaseA, ttl);
        Assert.False(refreshedA);

        // But leaseholder B can refresh
        var refreshedB = await _repo.RefreshProjectionLockAsync(sesionId, leaseB, ttl);
        Assert.True(refreshedB);
    }
}
