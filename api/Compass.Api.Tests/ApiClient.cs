using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Compass.Api.Tests;

public record MeResponse(Guid Id, string Email);

/// <summary>Clients, request helpers and response readers shared by the HTTP seam tests.</summary>
public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string DefaultPassword = "correct horse battery";

    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>A client that keeps its own cookie jar and never follows redirects.</summary>
    public static HttpClient CreateBrowserLikeClient(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>A client with no cookie jar: every request is anonymous unless a header is set by hand.</summary>
    public static HttpClient CreateAnonymousClient(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    public static Task<HttpResponseMessage> Register(this HttpClient client, string email, string password = DefaultPassword) =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password });

    /// <summary>Registers a fresh user on this client and returns who the API says they are.</summary>
    public static async Task<MeResponse> RegisterNewUser(this HttpClient client)
    {
        var response = await client.Register(UniqueEmail());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeResponse>(Json))!;
    }

    public static Task<HttpResponseMessage> Login(this HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>The raw <c>Set-Cookie</c> header for the session cookie, exactly as it went over the wire.</summary>
    public static string SessionSetCookie(this HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("compass_auth=", StringComparison.Ordinal));

    /// <summary>Asserts the flags every session cookie carries in every environment.</summary>
    public static void AssertSessionCookieFlags(string setCookie)
    {
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase); // persistent
    }

    public static async Task<JsonElement> Body(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);
}
