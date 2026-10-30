using System.Collections.Concurrent;

namespace ServiceScheduler.Api.Application;

/// <summary>
/// Serialises the check-then-insert critical section per dealership so two concurrent requests
/// can never be handed the same bay or technician. Different dealerships do not block each other.
/// Single-instance guarantee only: when scaling out, replace with a database-level guarantee
/// (PostgreSQL exclusion constraint on tstzrange, or advisory locks) - see the design document.
/// </summary>
public sealed class DealershipLock
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(int dealershipId, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(dealershipId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
