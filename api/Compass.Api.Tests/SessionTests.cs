using System.Net;
using System.Net.Http.Json;
using Compass.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Api.Tests;

public class SessionTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    [Fact]
    public async Task Unauthenticated_me_answers_401_with_no_redirect()
    {
        var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_session_survives_the_API_restarting_because_the_key_ring_lives_in_Postgres()
    {
        var email = Http.UniqueEmail();
        var registered = await factory.CreateAnonymousClient().Register(email);
        var cookie = registered.SessionSetCookie().Split(';')[0];

        // A second host over the same database: a fresh process with a fresh Data Protection
        // instance, which can only decrypt the cookie if it reads the same key ring.
        using var restarted = factory.WithWebHostBuilder(_ => { });
        var client = restarted.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, (await me.Content.ReadFromJsonAsync<MeResponse>(Http.Json))!.Email);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotEmpty(await db.DataProtectionKeys.AsNoTracking().ToListAsync());
    }
}
