using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace Data.Configuration;

/// <summary>
/// Answers "did anything actually set this key, and what?" by asking the configuration
/// providers directly (#1034).
///
/// <para>
/// Binding is lossy: once <c>IOptions&lt;T&gt;</c> holds <c>50</c>, nothing in the value says
/// whether that came from an environment variable, from <c>appsettings.json</c>, or from the
/// property initialiser in the class. The providers still know, so the origin is read back
/// from them rather than reconstructed.
/// </para>
/// </summary>
public static class ConfigurationOriginResolver
{
    /// <summary>
    /// Returns the friendly name of the last provider that supplies <paramref name="key"/>, or
    /// null when no provider does.
    ///
    /// <para>
    /// Providers are walked in reverse because that is the precedence the binder uses — the
    /// last one registered wins, so the last one holding the key is the one whose value the
    /// process is running. The child-key probe is there for arrays and lists, which a provider
    /// exposes as <c>Section:Property:0</c>, <c>:1</c>… and never as the parent path itself.
    /// </para>
    /// </summary>
    /// <param name="root">
    /// The configuration root. Null-tolerant: a caller holding a plain <c>IConfiguration</c>
    /// that is not a root simply gets "no provider", which reads as a class default.
    /// </param>
    /// <param name="key">The fully-qualified key, e.g. <c>StorageHistory:DiskCapacityBytes</c>.</param>
    public static string? FindProvidingSource(IConfigurationRoot? root, string key)
    {
        if (root is null)
        {
            return null;
        }

        foreach (var provider in root.Providers.Reverse())
        {
            if (provider.TryGet(key, out _) || provider.GetChildKeys([], key).Any())
            {
                return Describe(provider);
            }
        }

        return null;
    }

    /// <summary>
    /// Names a provider the way an operator would look for it: the file they would edit, or
    /// the word "environment" for the variables the compose file sets.
    /// </summary>
    private static string Describe(IConfigurationProvider provider) => provider switch
    {
        // Covers user secrets too, which is a JSON provider over secrets.json.
        JsonConfigurationProvider json when !string.IsNullOrEmpty(json.Source.Path) =>
            Path.GetFileName(json.Source.Path),
        JsonConfigurationProvider => "json file",
        EnvironmentVariablesConfigurationProvider => "environment",
        MemoryConfigurationProvider => "in-memory",
        ChainedConfigurationProvider => "chained configuration",
        // Command line and anything else a host adds. Naming the type is enough to find it,
        // and beats adding a package reference to this project just to type-check a match.
        _ => provider.GetType().Name
    };
}
