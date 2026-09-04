using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Compass.Api.Tests;

public class RegisterTests(CompassApiFactory factory) : IClassFixture<CompassApiFactory>
{
    [Fact]
    public async Task Register_signs_the_new_user_in_and_answers_201_with_their_identity()
    {
        var client = factory.CreateBrowserLikeClient();
        var email = Http.UniqueEmail();

        var response = await client.Register(email);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>(Http.Json);
        Assert.NotNull(me);
        Assert.Equal(email, me.Email);
        Assert.NotEqual(Guid.Empty, me.Id);

        // Signed in: the same cookie jar is now accepted by the session check.
        var check = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", Http.Json);
        Assert.Equal(me, check);
    }

    [Fact]
    public async Task Register_sets_a_persistent_HttpOnly_SameSite_Lax_session_cookie()
    {
        var client = factory.CreateAnonymousClient();

        var response = await client.Register(Http.UniqueEmail());

        var setCookie = response.SessionSetCookie();
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        // Development is plain HTTP, so the Secure flag follows the request scheme and is absent here.
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_with_a_short_password_answers_ProblemDetails_with_a_password_error()
    {
        var client = factory.CreateAnonymousClient();

        var response = await client.Register(Http.UniqueEmail(), password: "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Body();
        var passwordErrors = body.GetProperty("errors").GetProperty("password").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(passwordErrors, e => e!.Contains("at least 8", StringComparison.OrdinalIgnoreCase));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Register_with_an_email_already_in_use_answers_ProblemDetails_with_an_email_error()
    {
        var client = factory.CreateAnonymousClient();
        var email = Http.UniqueEmail();
        await client.Register(email);

        var response = await client.Register(email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Body()).GetProperty("errors");
        Assert.Contains("taken", errors.GetProperty("email")[0].GetString(), StringComparison.OrdinalIgnoreCase);
        // UserName is the email, so Identity reports the duplicate twice; only one field should surface.
        Assert.Single(errors.EnumerateObject());
    }

    [Fact]
    public async Task Register_accepts_a_long_passphrase_with_no_composition_rules()
    {
        var client = factory.CreateAnonymousClient();

        var response = await client.Register(Http.UniqueEmail(), password: "all lowercase letters and spaces");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_a_non_JSON_body()
    {
        var client = factory.CreateAnonymousClient();

        var response = await client.PostAsync("/api/auth/register",
            new FormUrlEncodedContent([new("email", Http.UniqueEmail()), new("password", "correct horse battery")]));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}

public class ProductionCookieTests : IClassFixture<ProductionCookieTests.ProductionFactory>
{
    public sealed class ProductionFactory : CompassApiFactory
    {
        public ProductionFactory() => Environment = "Production";
    }

    private readonly ProductionFactory _factory;

    public ProductionCookieTests(ProductionFactory factory) => _factory = factory;

    [Fact]
    public async Task In_Production_the_session_cookie_is_always_Secure_even_over_a_plain_HTTP_request()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.Register(Http.UniqueEmail());

        var setCookie = response.SessionSetCookie();
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
