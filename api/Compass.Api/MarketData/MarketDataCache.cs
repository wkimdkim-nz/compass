using System.Collections.Concurrent;

namespace Compass.Api.MarketData;

/// <summary>
/// What the providers last said, held in memory: one copy per running task, lost on restart, and
/// never in the database (ADR 0010; why that still counts as "fetched live" is ADR 0004, amended).
/// A fetch that throws is not held, so an outage lasts as long as the provider's and no longer.
/// </summary>
public sealed class MarketDataCache(TimeProvider time)
{
    public static readonly TimeSpan QuoteTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FundamentalsTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan TickerMapTtl = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, object Value)> _entries = new();

    /// <param name="keep">Which answers are worth holding; every one, when not given.</param>
    public async Task<T> GetOrFetchAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetch, Func<T, bool>? keep = null)
        where T : notnull
    {
        if (_entries.TryGetValue(key, out var entry) && time.GetUtcNow() < entry.Expires)
        {
            return (T)entry.Value;
        }

        // Two requests that miss together both fetch, and the later answer stands: one user, and
        // providers whose rate limits are nowhere near (ADR 0010).
        var value = await fetch();
        if (keep?.Invoke(value) ?? true)
        {
            _entries[key] = (time.GetUtcNow() + ttl, value);
        }
        else
        {
            _entries.TryRemove(key, out _);
        }
        return value;
    }
}
