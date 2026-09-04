using System.Net.Http.Json;

namespace Compass.Api.Tests;

/// <summary>
/// The cookie flags that depend on the hosting environment rather than on any one endpoint.
/// Register is simply the cheapest request that mints a session cookie.
/// </summary>
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

        var response = await client.Register(ApiClient.UniqueEmail());

        var setCookie = response.SessionSetCookie();
        ApiClient.AssertSessionCookieFlags(setCookie);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
