namespace TrueMain.Http;

/// <summary>
/// The HTTP → HTTPS redirect, registered only where the host serves HTTPS. Every deployed stack binds
/// the API to plain HTTP (TLS ends at the edge, which never proxies to the API), so there the
/// middleware has no port to redirect to and only warns "Failed to determine the https port for
/// redirect" on each start. A local run on the <c>https</c> launch profile keeps it.
/// </summary>
internal static class HttpsRedirection
{
    /// <summary>
    /// Whether the host configuration names an HTTPS endpoint: an explicit <c>https_port</c>, an
    /// <c>https_ports</c> list (<c>ASPNETCORE_HTTPS_PORTS</c>), or an <c>https://</c> entry in
    /// <c>urls</c> (<c>ASPNETCORE_URLS</c>, <c>--urls</c>, a launch profile's <c>applicationUrl</c>).
    /// </summary>
    public static bool HasHttpsEndpoint(IConfiguration configuration)
        => !string.IsNullOrWhiteSpace(configuration["https_port"])
           || !string.IsNullOrWhiteSpace(configuration["https_ports"])
           || (configuration["urls"]?.Contains("https://", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Adds <c>UseHttpsRedirection</c> when <see cref="HasHttpsEndpoint"/> holds, and nothing otherwise.</summary>
    public static WebApplication UseHttpsRedirectionWhenServed(this WebApplication app)
    {
        if (HasHttpsEndpoint(app.Configuration))
        {
            app.UseHttpsRedirection();
        }

        return app;
    }
}
