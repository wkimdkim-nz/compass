using System.Net;
using System.Net.Http.Json;
using Compass.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Api.Tests;

public record NoteResponse(Guid Id, Guid UserId, string Text, DateTimeOffset CreatedAt);

public class NoteTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    private static Task<HttpResponseMessage> AddNote(HttpClient client, string text) =>
        client.PostAsJsonAsync("/api/notes", new { text });

    private static Task<List<NoteResponse>?> ListNotes(HttpClient client) =>
        client.GetFromJsonAsync<List<NoteResponse>>("/api/notes", ApiClient.Json);

    [Fact]
    public async Task A_signed_in_user_can_write_a_note_and_read_it_back_scoped_to_their_own_id()
    {
        var client = factory.CreateBrowserLikeClient();
        var me = await client.RegisterNewUser();

        var created = await AddNote(client, "hello from the seam");
        var notes = await ListNotes(client);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var note = await created.Content.ReadFromJsonAsync<NoteResponse>(ApiClient.Json);
        Assert.Equal(me.Id, note!.UserId);
        Assert.Equal("hello from the seam", note.Text);
        Assert.Equal([note], notes);

        // The persisted user_id is the caller's claim, not merely echoed back by the endpoint.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        Assert.Equal(me.Id, persisted.UserId);
    }

    [Fact]
    public async Task A_state_changing_POST_succeeds_carrying_only_the_session_cookie()
    {
        // The executable record of "no antiforgery": the session cookie is the only thing the
        // browser has, and nothing is attached by hand.
        var client = factory.CreateBrowserLikeClient();
        var registered = await client.Register(ApiClient.UniqueEmail());
        Assert.Single(registered.Headers.GetValues("Set-Cookie")); // only compass_auth, no token cookie
        Assert.Empty(client.DefaultRequestHeaders);

        var created = await AddNote(client, "no token attached");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Notes_are_read_back_newest_first()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.Register(ApiClient.UniqueEmail());

        await AddNote(client, "first");
        await AddNote(client, "second");
        var notes = await ListNotes(client);

        Assert.Equal(["second", "first"], notes!.Select(n => n.Text));
    }

    [Fact]
    public async Task One_user_cannot_see_another_users_notes()
    {
        var alice = factory.CreateBrowserLikeClient();
        await alice.Register(ApiClient.UniqueEmail());
        await AddNote(alice, "alice's note");
        var bob = factory.CreateBrowserLikeClient();
        await bob.Register(ApiClient.UniqueEmail());

        var bobsNotes = await ListNotes(bob);

        Assert.Empty(bobsNotes!);
    }

    [Fact]
    public async Task Signed_out_visitors_are_refused_with_401_on_both_read_and_write()
    {
        var client = factory.CreateAnonymousClient();

        var read = await client.GetAsync("/api/notes");
        var write = await AddNote(client, "should not land");

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
        Assert.Null(read.Headers.Location);
        Assert.Null(write.Headers.Location);
    }

    [Fact]
    public async Task An_empty_note_answers_ProblemDetails_with_a_text_error()
    {
        var client = factory.CreateBrowserLikeClient();
        await client.Register(ApiClient.UniqueEmail());

        var response = await AddNote(client, "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Body()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task Deleting_a_user_deletes_their_notes_in_the_database()
    {
        var client = factory.CreateBrowserLikeClient();
        var me = await client.RegisterNewUser();
        await AddNote(client, "will be cascaded away");

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(me.Id.ToString());
            Assert.True((await users.DeleteAsync(user!)).Succeeded);
        }

        // Asked of Postgres directly, with a fresh context: the cascade fired in the database,
        // not in EF's change tracker.
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = await db.Notes.AsNoTracking().CountAsync(n => n.UserId == me.Id);
        Assert.Equal(0, remaining);
    }
}
