using Microsoft.Extensions.Caching.Memory;

namespace MomentApp.Services;

/// <summary>
/// Per-participant send throttle for chat messages.
/// </summary>
/// <remarks>
/// Replaces a <c>static Dictionary&lt;string, DateTime&gt;</c> that was written from multiple
/// hub invocation threads without synchronisation. That is worse than a race: concurrent
/// writes can corrupt a dictionary's bucket chain so that a later read spins forever, taking
/// the process with it. Its cleanup pass also never matched anything (it filtered keys by
/// connection id against keys built from participant id), so the dictionary grew unbounded.
///
/// Entries here expire on their own, so there is no sweep and nothing to leak.
/// </remarks>
public sealed class MessageRateLimiter
{
    private const int BurstCapacity = 5;
    private const double RefillPerSecond = 1.0;
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(2);

    private readonly IMemoryCache _cache;

    public MessageRateLimiter(IMemoryCache cache) => _cache = cache;

    private sealed class Bucket
    {
        public double Tokens;
        public DateTime LastRefill;
    }

    /// <summary>
    /// Attempts to consume one send allowance. Returns false when the caller is sending
    /// too quickly.
    /// </summary>
    /// <remarks>
    /// A token bucket rather than a fixed 1-per-second gate: a burst of a few messages is
    /// normal human behaviour in a chat and shouldn't be rejected, while a sustained flood
    /// still settles to one per second.
    /// </remarks>
    public bool TryAcquire(string roomId, string participantId)
    {
        var key = $"rate:{roomId}:{participantId}";
        var bucket = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = EntryLifetime;
            return new Bucket { Tokens = BurstCapacity, LastRefill = DateTime.UtcNow };
        })!;

        lock (bucket)
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - bucket.LastRefill).TotalSeconds;
            if (elapsed > 0)
            {
                bucket.Tokens = Math.Min(BurstCapacity, bucket.Tokens + elapsed * RefillPerSecond);
                bucket.LastRefill = now;
            }

            if (bucket.Tokens < 1.0)
            {
                return false;
            }

            bucket.Tokens -= 1.0;
            return true;
        }
    }
}
