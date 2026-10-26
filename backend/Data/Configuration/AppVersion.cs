using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Data.Configuration;

/// <summary>
/// The release a backend process runs (#2297). The deploy passes it as <c>AppVersion</c> — the
/// same deploy-time <c>APP_VERSION</c> that labels the site's footer, not a build arg, so one
/// image stays promotable from preprod to prod.
/// </summary>
public static class AppVersion
{
    /// <summary>The configuration key the compose files set from <c>APP_VERSION</c>.</summary>
    public const string ConfigurationKey = "AppVersion";

    /// <summary>
    /// The deployed release, or null when the deploy did not state one (a local run, a manual
    /// redeploy). Deliberately no assembly fallback: every build carries the same
    /// <c>1.0.0</c> informational version, so it would name a release that does not exist.
    /// </summary>
    public static string? FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[ConfigurationKey]?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// <see cref="FromConfiguration"/>, falling back to the entry assembly's informational
    /// version (trimmed of the SDK's source-revision suffix) for a page that would rather show
    /// something than nothing.
    /// </summary>
    public static string? Resolve(IConfiguration configuration)
        => FromConfiguration(configuration) ?? ReadInformationalVersion();

    private static string? ReadInformationalVersion()
    {
        var informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return null;
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
