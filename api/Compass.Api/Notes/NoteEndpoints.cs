using System.Security.Claims;
using Compass.Api.Auth;
using Compass.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Compass.Api.Notes;

public record NoteRequest(string? Text);

public record NoteResponse(Guid Id, Guid UserId, string Text, DateTimeOffset CreatedAt);

/// <summary>
/// ⚠️ THROWAWAY, with <see cref="Note"/>. The user-scoped write and read that prove the seam.
/// Every query is filtered by the caller's user id; there is no way to address another user's row.
/// </summary>
public static class NoteEndpoints
{
    public static IEndpointRouteBuilder MapNoteEndpoints(this IEndpointRouteBuilder routes)
    {
        var notes = routes.MapGroup("/api/notes").RequireAuthorization();

        notes.MapGet("/", async (ClaimsPrincipal caller, AppDbContext db) =>
            Results.Ok(await db.Notes
                .Where(n => n.UserId == caller.UserId())
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => n.ToResponse())
                .ToListAsync()));

        notes.MapPost("/", async (NoteRequest body, ClaimsPrincipal caller, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
            {
                return TextError("Text is required.");
            }
            if (body.Text.Length > Note.MaxTextLength)
            {
                return TextError($"Text must be at most {Note.MaxTextLength} characters.");
            }

            var note = new Note
            {
                Id = Guid.CreateVersion7(),
                UserId = caller.UserId(),
                Text = body.Text,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            return Results.Created($"/api/notes/{note.Id}", note.ToResponse());
        });

        return routes;
    }

    /// <summary>A ProblemDetails body whose one field error is <c>text</c>, the only field the client supplies.</summary>
    private static IResult TextError(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = [message] });

    private static NoteResponse ToResponse(this Note note) => new(note.Id, note.UserId, note.Text, note.CreatedAt);
}
