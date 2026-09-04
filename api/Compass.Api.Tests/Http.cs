using System.Net.Http.Json;
using System.Text.Json;

namespace Compass.Api.Tests;

/// <summary>Request/response shapes and small helpers shared by the seam tests.</summary>
public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static Task<HttpResponseMessage> Register(this HttpClient client, string email, string password = "correct horse battery") =>
        client.PostAsJsonAsync("/api/auth/register", new { email, password });

    public static Task<HttpResponseMessage> Login(this HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>The raw <c>Set-Cookie</c> header for the session cookie, exactly as it went over the wire.</summary>
    public static string SessionSetCookie(this HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("compass_auth=", StringComparison.Ordinal));

    public static async Task<JsonElement> Body(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json));
}

public record MeResponse(Guid Id, string Email);
