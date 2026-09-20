namespace Compass.Api.Data;

public static class PostgresClock
{
    /// <summary>
    /// The current time at the precision Postgres keeps. <c>timestamptz</c> stores microseconds
    /// and .NET's clock counts 100ns ticks, so an untruncated value would be echoed by the request
    /// that stamped a row but read back differently by the next. Linux clocks do produce
    /// sub-microsecond ticks; macOS ones happen not to, which is why this only ever showed up on
    /// the CI runner. Anything that stamps a row and answers with the stamp takes it from here.
    /// </summary>
    public static DateTimeOffset PostgresNow(this TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }
}
