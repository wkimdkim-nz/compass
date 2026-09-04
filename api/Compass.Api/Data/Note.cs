namespace Compass.Api.Data;

/// <summary>
/// ⚠️ THROWAWAY. The Phase 0 placeholder row that proves an authenticated, user-scoped write and
/// read reach Postgres. It is deleted when Phase 1's first real entity (a projection) lands —
/// do not grow it toward that entity, and do not build on it.
/// </summary>
public class Note
{
    public const int MaxTextLength = 1000;

    public Guid Id { get; set; }

    /// <summary>Scalar foreign key to <c>"AspNetUsers".id</c>. No navigation property, on purpose.</summary>
    public Guid UserId { get; set; }

    public string Text { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
