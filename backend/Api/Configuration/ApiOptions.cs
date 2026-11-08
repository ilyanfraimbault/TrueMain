using Data.Configuration;
using Microsoft.Extensions.Options;

namespace TrueMain.Configuration;

/// <summary>
/// An options class the Api binds from one configuration section. Its startup rules live on the
/// class itself, as data annotations and <see cref="System.ComponentModel.DataAnnotations.IValidatableObject"/>,
/// so a new knob and its check are one edit.
/// </summary>
public interface IApiOptions
{
    static abstract string Section { get; }

    /// <summary>The section's entry on the admin configuration page (#1034), or null when it is not shown.</summary>
    static virtual EffectiveConfigurationSectionDescriptor? ConfigurationPage => null;
}

public static class ApiOptionsServiceCollectionExtensions
{
    /// <summary>
    /// Binds <typeparamref name="TOptions"/>, validates it at boot and lists it on the configuration
    /// page. Every feature registers the options it reads; the first call wins, so a section shared
    /// by several features is bound once (binding twice would append to its list properties).
    /// </summary>
    public static IServiceCollection AddApiOptions<TOptions>(this IServiceCollection services)
        where TOptions : class, IApiOptions
        => services.AddOptionsSection<TOptions>(TOptions.Section, TOptions.ConfigurationPage);

    /// <summary>The same, for an options class another project owns.</summary>
    public static IServiceCollection AddOptionsSection<TOptions>(
        this IServiceCollection services,
        string section,
        EffectiveConfigurationSectionDescriptor? configurationPage = null,
        Action<OptionsBuilder<TOptions>>? rules = null)
        where TOptions : class
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(BoundSection<TOptions>)))
        {
            return services;
        }

        services.AddSingleton<BoundSection<TOptions>>();
        var builder = services.AddOptions<TOptions>().BindConfiguration(section).ValidateDataAnnotations();
        rules?.Invoke(builder);
        builder.ValidateOnStart();
        if (configurationPage is not null)
        {
            services.AddSingleton(configurationPage);
        }

        return services;
    }

    private sealed class BoundSection<TOptions>;
}
