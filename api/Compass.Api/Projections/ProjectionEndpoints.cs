using System.Security.Claims;
using System.Text.Json.Serialization;
using Compass.Api.Auth;
using Compass.Api.Data;
using Compass.Api.Tickers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Compass.Api.Projections;

public sealed record ExitMultipleResponse(string Kind, double Value);

/// <param name="Outputs">What the .NET calculator gave on the save this answers. Null on a read: there is nothing stored to give, and the base facts to compute against may not be there.</param>
public sealed record ScenarioResponse(
    string Name,
    double GrowthRate,
    double FutureNetMargin,
    ExitMultipleResponse ExitMultiple,
    double DilutionRate,
    int Horizon,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ProjectionOutputs? Outputs);

/// <summary>What a create and a replace answer with: the saved record, and each scenario's recomputed outputs.</summary>
public sealed record SavedProjectionResponse(
    string Ticker,
    string CompanyName,
    long? Cik,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ScenarioResponse> Scenarios);

/// <summary>What a read answers with: the saved record over whatever the ticker lookup has for it today.</summary>
/// <param name="BaseFacts">Live, from the ticker lookup. Null when it could not seed; <paramref name="BaseFactsUnavailable"/> then says why.</param>
/// <param name="Missing">As the ticker lookup's: the base facts that could not be fetched although the rest were.</param>
/// <param name="BaseFactsUnavailable">The lookup's plain reason (unknown ticker, unsupported filer, provider down), or null.</param>
public sealed record LoadedProjectionResponse(
    string Ticker,
    string CompanyName,
    long? Cik,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ScenarioResponse> Scenarios,
    BaseFactsResponse? BaseFacts,
    IReadOnlyList<string> Missing,
    string? BaseFactsUnavailable);

/// <summary>
/// Saving, replacing and loading a projection (ADR 0004, amended). Create and replace are two
/// verbs so that a client which skipped the confirmation cannot overwrite silently. Every query is
/// filtered by the caller's user id, so another user's projection is a 404 like one that is not there.
/// </summary>
public static class ProjectionEndpoints
{
    private const string UniqueViolation = "23505";

    public static IEndpointRouteBuilder MapProjectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var projections = routes.MapGroup("/api/projections").RequireAuthorization();

        projections.MapPost("/", async (SaveProjectionRequest body, ClaimsPrincipal caller, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var result = SaveProjection.Validate(body);
            if (result is not SaveProjectionResult.Valid valid) return Invalid(result);

            var now = clock.PostgresNow();
            var projection = new Projection
            {
                Id = Guid.CreateVersion7(),
                UserId = caller.UserId(),
                Ticker = valid.Ticker,
                CompanyName = valid.CompanyName,
                Cik = valid.Cik,
                CreatedAt = now,
                UpdatedAt = now,
                Scenarios = [.. valid.Scenarios],
            };
            db.Projections.Add(projection);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            // The unique index is the check: asking first would leave two creates racing between the question and the insert.
            catch (DbUpdateException failure) when (failure.InnerException is PostgresException { SqlState: UniqueViolation })
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    detail: $"You already have a projection for {valid.Ticker}. Replace it instead.");
            }

            return Results.Created($"/api/projections/{Uri.EscapeDataString(projection.Ticker)}", projection.ToSavedResponse(valid));
        });

        projections.MapPut("/{ticker}", async (string ticker, SaveProjectionRequest body, ClaimsPrincipal caller, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            if (await db.FindOwnAsync(caller, ticker, cancellationToken) is not { } projection) return NoProjection(ticker);

            var result = SaveProjection.Validate(body, routeTicker: ticker);
            if (result is not SaveProjectionResult.Valid valid) return Invalid(result);

            projection.CompanyName = valid.CompanyName;
            projection.Cik = valid.Cik;
            projection.UpdatedAt = clock.PostgresNow();
            db.Scenarios.RemoveRange(projection.Scenarios);
            projection.Scenarios = [.. valid.Scenarios];
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            // Two replaces at once: the other's scenarios went in, or its deletes came first. Its answer stands.
            catch (DbUpdateException failure) when (failure is DbUpdateConcurrencyException || failure.InnerException is PostgresException { SqlState: UniqueViolation })
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    detail: $"Your projection for {projection.Ticker} was replaced at the same moment. Load it again before replacing it.");
            }

            return Results.Ok(projection.ToSavedResponse(valid));
        });

        projections.MapGet("/{ticker}", async (string ticker, ClaimsPrincipal caller, AppDbContext db, TickerLookup lookup, CancellationToken cancellationToken) =>
        {
            if (await db.FindOwnAsync(caller, ticker, cancellationToken) is not { } projection) return NoProjection(ticker);

            // Whatever the lookup says, the saved assumptions are answered: this route's 404 is the one above.
            var (baseFacts, missing, unavailable) = await lookup.LookUpAsync(projection.Ticker, cancellationToken) switch
            {
                TickerLookupResult.Found found => (found.Response.BaseFacts, found.Response.Missing, null),
                TickerLookupResult.Failed failed => ((BaseFactsResponse?)null, (IReadOnlyList<string>)[], failed.Reason),
                var other => throw new InvalidOperationException($"Unhandled lookup result {other.GetType().Name}."),
            };

            return Results.Ok(new LoadedProjectionResponse(
                projection.Ticker,
                projection.CompanyName,
                projection.Cik,
                projection.CreatedAt,
                projection.UpdatedAt,
                projection.ScenarioResponses(outputsByName: null),
                baseFacts,
                missing,
                unavailable));
        });

        return routes;
    }

    private static Task<Projection?> FindOwnAsync(this AppDbContext db, ClaimsPrincipal caller, string ticker, CancellationToken cancellationToken)
    {
        var userId = caller.UserId();
        var normalised = Projection.NormaliseTicker(ticker);
        return db.Projections
            .Include(p => p.Scenarios)
            .SingleOrDefaultAsync(p => p.UserId == userId && p.Ticker == normalised, cancellationToken);
    }

    private static IResult NoProjection(string ticker) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        detail: $"You have no saved projection for {Projection.NormaliseTicker(ticker)}.");

    private static IResult Invalid(SaveProjectionResult result) =>
        Results.ValidationProblem(((SaveProjectionResult.Invalid)result).Errors);

    private static SavedProjectionResponse ToSavedResponse(this Projection projection, SaveProjectionResult.Valid valid) => new(
        projection.Ticker,
        projection.CompanyName,
        projection.Cik,
        projection.CreatedAt,
        projection.UpdatedAt,
        projection.ScenarioResponses(valid.OutputsByName));

    /// <summary>Bear, Base, Bull, whatever order the rows came back in.</summary>
    private static IReadOnlyList<ScenarioResponse> ScenarioResponses(this Projection projection, IReadOnlyDictionary<string, ProjectionOutputs>? outputsByName) =>
    [
        .. projection.Scenarios
            .OrderBy(s => Scenario.Names.IndexOf(s.Name))
            .Select(s => new ScenarioResponse(
                s.Name,
                (double)s.GrowthRate,
                (double)s.FutureNetMargin,
                new ExitMultipleResponse(s.ExitMultipleKind, (double)s.ExitMultipleValue),
                (double)s.DilutionRate,
                s.Horizon,
                outputsByName?[s.Name])),
    ];
}
