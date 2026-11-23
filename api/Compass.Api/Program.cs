using Compass.Api;
using Compass.Api.Auth;
using Compass.Api.Data;
using Compass.Api.Health;
using Compass.Api.MarketData;
using Compass.Api.Projections;
using Compass.Api.Tickers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(DatabaseConnectionString.Resolve(builder.Configuration))
           .UseSnakeCaseNamingConvention());

// The key ring that encrypts the session cookie lives in the same database as the users, in
// every environment, so the keys and the accounts they authenticate share one lifetime (ADR 0007).
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("Compass");

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        // Length only, no composition rules (NIST 800-63B).
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "compass_auth";
    options.Cookie.HttpOnly = true;
    // SameSite=Lax alone is the CSRF defence (ADR 0007, amended). Same-site cookie delivery is
    // what makes that hold: a Vite proxy locally, CloudFront path-routing in production.
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Local development is plain HTTP; production is always HTTPS.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;

    // Identity's default is a 302 to /Account/Login, which an SPA `fetch` cannot act on.
    // Answer with a bare status code and a ProblemDetails body instead.
    options.Events.OnRedirectToLogin = ctx => ctx.HttpContext.WriteProblemAsync(StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = ctx => ctx.HttpContext.WriteProblemAsync(StatusCodes.Status403Forbidden);
});

// One setting per market-data port, under the key names ADR 0010 records. Resolved here, before
// the host is built, so an environment that may not run on stubs never gets as far as listening —
// the same place and the same way a missing connection string stops it.
var marketDataProviders = MarketDataProviders.Resolve(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(marketDataProviders);
builder.Services.Configure<FinnhubOptions>(builder.Configuration.GetSection(FinnhubOptions.SectionName));
builder.Services.Configure<EdgarOptions>(builder.Configuration.GetSection(EdgarOptions.SectionName));
// Registered whichever way the fundamentals port resolved: nothing constructs it, and so nothing
// reads the contact address, until the real fundamentals source asks for it.
builder.Services.AddEdgarClient();
builder.Services.AddMarketDataPorts(marketDataProviders);
builder.Services.AddScoped<TickerLookup>();

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
    {
        // A login failure must be byte-identical whatever went wrong (anti-enumeration), so no
        // per-request trace id on 401s.
        if (ctx.ProblemDetails.Status == StatusCodes.Status401Unauthorized)
        {
            ctx.ProblemDetails.Extensions.Remove("traceId");
        }
    });

// Deliberately absent: any CORS policy. Development and production are both single-origin,
// and registering CORS with credentials would break a precondition of the CSRF design (ADR 0007).

var app = builder.Build();

marketDataProviders.LogStubs(app.Logger);

// Deliberately absent: UseHttpsRedirection(). CloudFront reaches the origin over HTTPS and the
// load balancer terminates it there (ADR 0009); the last hop, load balancer to task, is plain
// HTTP. So a redirect here would bounce every health check into a 307 loop and the service would
// never come up. The session cookie is unaffected — CookieSecurePolicy.Always does not consult
// the request's scheme.

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapProjectionEndpoints();
app.MapTickerEndpoints();

app.Run();

// Migrations are applied explicitly, never here on startup: `./efbundle` as a one-off task when
// deployed, `dotnet ef database update` locally.
