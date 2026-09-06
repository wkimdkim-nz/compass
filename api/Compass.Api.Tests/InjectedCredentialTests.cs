using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Npgsql;

namespace Compass.Api.Tests;

/// <summary>
/// The production configuration path, walked locally.
/// </summary>
/// <remarks>
/// ECS hands the API its database credentials in three pieces: a plain
/// <c>ConnectionStrings__Default</c> that carries only the non-secret half, and <c>DB_USERNAME</c>
/// and <c>DB_PASSWORD</c> injected individually from the RDS-managed secret. Nothing joins them but
/// the app itself. This fixture boots the app in exactly that shape and then asks it to do the one
/// thing that proves the join happened: reach the database.
///
/// The reverse case — no overlay present, credentials already in the connection string — is the
/// shape every other test class here runs on, so the suite covers it rather than repeating it.
/// </remarks>
public class InjectedCredentialTests(InjectedCredentialTests.SplitCredentialFactory factory)
    : IClassFixture<InjectedCredentialTests.SplitCredentialFactory>
{
    /// <summary>Splits the container's connection string the way ECS splits RDS's.</summary>
    public sealed class SplitCredentialFactory : CompassApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            var whole = new NpgsqlConnectionStringBuilder(ConnectionString);
            var username = whole.Username;
            var password = whole.Password;
            whole.Username = null;
            whole.Password = null;

            builder.UseSetting("ConnectionStrings:Default", whole.ConnectionString);
            builder.UseSetting("DB_USERNAME", username);
            builder.UseSetting("DB_PASSWORD", password);
        }
    }

    [Fact]
    public async Task Credentials_arriving_beside_the_connection_string_still_reach_the_database()
    {
        var client = factory.CreateBrowserLikeClient();

        var me = await client.RegisterNewUser();
        var created = await client.PostAsJsonAsync("/api/notes", new { text = "joined at startup" });
        var notes = await client.GetFromJsonAsync<List<NoteResponse>>("/api/notes", ApiClient.Json);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var note = Assert.Single(notes!);
        Assert.Equal(me.Id, note.UserId);
        Assert.Equal("joined at startup", note.Text);
    }
}
