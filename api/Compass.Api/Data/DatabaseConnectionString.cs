using Npgsql;

namespace Compass.Api.Data;

/// <summary>
/// The one path by which the API settles how it reaches its database — the credentials it connects
/// with, and the transport it will not attempt. The same code runs in every environment;
/// production is not a branch.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConnectionStrings:Default</c> always carries the non-secret half — host, port, database, and
/// in production the two settings documented below. <c>DB_USERNAME</c> and <c>DB_PASSWORD</c> are
/// overlaid onto it when they are present and ignored when they are not. That is a test of
/// presence, never of environment: locally the credentials are already inside the committed
/// development connection string and nothing is injected, so the overlay finds nothing and does
/// nothing; in production ECS injects the two keys of the RDS-managed secret individually
/// (<c>&lt;arn&gt;:username::</c> and <c>&lt;arn&gt;:password::</c>) and the overlay joins them on.
/// The path that runs in production is therefore the path the local tests exercise.
/// </para>
/// <para>
/// The credentials are split this way because RDS owns the master password
/// (<c>manage_master_user_password = true</c>), so it exists only as a two-key JSON secret and
/// never enters Terraform state. Writing a whole connection string into a second Terraform-managed
/// secret would put the password back into state and undo that (spec
/// <see href="https://github.com/wkimdkim-nz/compass/issues/28">#28</see>).
/// </para>
/// <para>
/// <b>What production must put in <c>ConnectionStrings__Default</c>, and why.</b> Two of its
/// settings are load-bearing and are supplied by the task definition, not by this code — recorded
/// here because nothing at the AWS end explains them:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>Trust Server Certificate=true</c> — Npgsql 8 changed <c>SSL Mode=Require</c> to validate the
/// server's certificate, and RDS's certificate authority is not in the runtime image's trust store.
/// Without it the connection does not degrade, it fails outright. <c>Verify-Full</c> with a
/// baked-in RDS CA bundle was rejected: a second thing expiring on AWS's schedule.
/// </description></item>
/// <item><description>
/// <c>Maximum Pool Size=20</c> — Npgsql defaults to 100 and a <c>db.t4g.micro</c> tops out near
/// 100. A deployment runs the old and the new task concurrently, so the default would overshoot
/// the database's connection ceiling exactly while deploying.
/// </description></item>
/// </list>
/// <para>
/// One setting is decided here rather than by configuration: <b>GSS/Kerberos encryption is off</b>,
/// because the runtime image carries no Kerberos libraries and Compass never authenticates that
/// way. Left on, Npgsql probes for them on the first connection of every process and fails,
/// writing an error line about nothing to stderr on every task start and every migration run. It
/// is unconditional for the same reason the overlay is: an environment that behaved differently
/// would be an environment the tests do not cover.
/// </para>
/// </remarks>
public static class DatabaseConnectionString
{
    /// <summary>The configuration name of the connection string, in every environment.</summary>
    private const string Name = "Default";

    /// <summary>Configuration keys ECS injects the RDS-managed secret's two JSON keys into.</summary>
    private const string UsernameKey = "DB_USERNAME";
    private const string PasswordKey = "DB_PASSWORD";

    /// <summary>
    /// The connection string to open: the configured one, with GSS/Kerberos ruled out and any
    /// separately supplied credentials overlaid onto it.
    /// </summary>
    public static string Resolve(IConfiguration configuration)
    {
        var connection = new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString(Name)
            ?? throw new InvalidOperationException(
                $"Connection string '{Name}' is not configured. In production it arrives as the " +
                $"environment variable ConnectionStrings__{Name}."));

        // The runtime image ships no Kerberos libraries and Compass never authenticates that way,
        // so stop Npgsql probing for them. Left on, the probe fails on the first connection of
        // every process and writes "Cannot load library libgssapi_krb5.so.2" to stderr — an error
        // line about nothing, on every task start and every migration run.
        connection.GssEncryptionMode = GssEncryptionMode.Disable;

        if (configuration[UsernameKey] is { Length: > 0 } username)
        {
            connection.Username = username;
        }
        if (configuration[PasswordKey] is { Length: > 0 } password)
        {
            connection.Password = password;
        }

        return connection.ConnectionString;
    }
}
