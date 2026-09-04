using Microsoft.AspNetCore.Mvc;

namespace Compass.Api;

public static class ProblemResponses
{
    /// <summary>Writes a ProblemDetails body for <paramref name="statusCode"/> to the current response.</summary>
    public static async Task WriteProblemAsync(this HttpContext http, int statusCode)
    {
        http.Response.StatusCode = statusCode;
        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = statusCode },
        });
    }
}
