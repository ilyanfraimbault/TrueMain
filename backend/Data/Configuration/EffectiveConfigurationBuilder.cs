using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Data.Configuration;

/// <summary>
/// Reads a host's own bound options back out of its container and renders them for the admin
/// configuration page (#1034).
///
/// <para>
/// The values come from <c>IOptions&lt;T&gt;</c>, which is the same singleton every consumer in
/// the process injects — so what the page shows is definitionally what the code runs on,
/// post-validation and post-<c>PostConfigure</c>. Nothing here re-reads a settings file; the
/// configuration providers are consulted only to answer where a value came from.
/// </para>
/// </summary>
public static class EffectiveConfigurationBuilder
{
    /// <summary>
    /// Builds the snapshot for <paramref name="catalog"/>'s sections.
    ///
    /// <para>
    /// A section whose <c>IOptions&lt;T&gt;</c> is not registered in this container is skipped
    /// rather than throwing: a catalog is a static declaration, and a host that stops binding a
    /// section should lose a card on a page, not fail to boot.
    /// </para>
    /// </summary>
    public static EffectiveConfigurationSnapshot Build(
        EffectiveConfigurationCatalog catalog,
        IServiceProvider services,
        IConfiguration configuration,
        string environmentName,
        DateTime capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(services);

        var root = configuration as IConfigurationRoot;
        var sections = new List<EffectiveConfigurationSection>(catalog.Sections.Count);

        foreach (var descriptor in catalog.Sections)
        {
            var options = ResolveOptionsValue(services, descriptor.OptionsType);
            if (options is null)
            {
                continue;
            }

            sections.Add(BuildSection(descriptor, options, root));
        }

        return new EffectiveConfigurationSnapshot
        {
            ProcessName = catalog.ProcessName,
            Environment = environmentName,
            Version = ReadInformationalVersion(),
            CapturedAtUtc = capturedAtUtc,
            Sections = sections
        };
    }

    /// <summary>
    /// Resolves <c>IOptions&lt;T&gt;.Value</c> for <paramref name="optionsType"/>, or null when the
    /// host does not bind that section. Reflection rather than <c>dynamic</c>: the runtime binder
    /// would drag another dependency into this project for no gain.
    /// </summary>
    private static object? ResolveOptionsValue(IServiceProvider services, Type optionsType)
    {
        var accessorType = typeof(IOptions<>).MakeGenericType(optionsType);
        var accessor = services.GetService(accessorType);

        return accessor is null
            ? null
            : accessorType.GetProperty(nameof(IOptions<object>.Value))?.GetValue(accessor);
    }

    private static EffectiveConfigurationSection BuildSection(
        EffectiveConfigurationSectionDescriptor descriptor,
        object options,
        IConfigurationRoot? root)
    {
        // The class defaults, to tell "nobody set this" from "something computed it at boot".
        // A type without a usable parameterless constructor simply loses that distinction:
        // every unsupplied key then reads as a default, which is the honest fallback.
        object? defaults = null;
        try
        {
            defaults = Activator.CreateInstance(descriptor.OptionsType);
        }
        catch (MissingMethodException)
        {
            // No parameterless constructor.
        }

        var values = new List<EffectiveConfigurationValue>();

        foreach (var property in ReadableProperties(descriptor))
        {
            // Belt and braces. The section allow-list is what keeps credentials out of the
            // response; this is the backstop for a mis-authored catalog entry, and a guard test
            // asserts it never has to fire.
            if (EffectiveConfigurationRedaction.IsSecretName(property.Name))
            {
                continue;
            }

            var key = descriptor.SectionName + ":" + property.Name;
            var raw = property.GetValue(options);
            var rendered = ConfigurationValueRenderer.Render(property.Name, raw);
            var source = ConfigurationOriginResolver.FindProvidingSource(root, key);

            values.Add(new EffectiveConfigurationValue
            {
                Key = key,
                Name = property.Name,
                Value = rendered.Value,
                ValueLabel = rendered.ValueLabel,
                Unit = rendered.Unit,
                Origin = ResolveOrigin(source, property, defaults, rendered),
                Source = source,
                Notice = ResolveNotice(descriptor, property.Name, raw)
            });
        }

        return new EffectiveConfigurationSection
        {
            Name = descriptor.SectionName,
            Title = descriptor.Title,
            Description = descriptor.Description,
            Values = values
        };
    }

    private static string ResolveOrigin(
        string? source,
        PropertyInfo property,
        object? defaults,
        RenderedConfigurationValue rendered)
    {
        if (source is not null)
        {
            return EffectiveConfigurationOrigins.Override;
        }

        if (defaults is null)
        {
            return EffectiveConfigurationOrigins.Default;
        }

        var defaultRendered = ConfigurationValueRenderer.Render(property.Name, property.GetValue(defaults));

        // Compare the rendered strings, not the objects: a List of the same three regions is a
        // different reference every time, and reference inequality would report every list-typed
        // option as derived.
        return string.Equals(defaultRendered.Value, rendered.Value, StringComparison.Ordinal)
            ? EffectiveConfigurationOrigins.Default
            : EffectiveConfigurationOrigins.Derived;
    }

    /// <summary>
    /// The properties a descriptor exposes, in declaration order.
    ///
    /// <para>
    /// Ordering by metadata token is what keeps <c>DataQualityDetectors</c>' thirty knobs grouped
    /// by detector the way the class declares them; alphabetical would interleave five unrelated
    /// detectors. It is a CLR implementation detail rather than a guarantee, which is acceptable
    /// because it only affects presentation order.
    /// </para>
    /// </summary>
    private static IEnumerable<PropertyInfo> ReadableProperties(
        EffectiveConfigurationSectionDescriptor descriptor)
    {
        var properties = descriptor.OptionsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken);

        if (descriptor.IncludeProperties is null)
        {
            return properties;
        }

        var included = descriptor.IncludeProperties.ToHashSet(StringComparer.Ordinal);
        return properties.Where(property => included.Contains(property.Name));
    }

    private static string? ResolveNotice(
        EffectiveConfigurationSectionDescriptor descriptor,
        string propertyName,
        object? value)
    {
        foreach (var notice in descriptor.Notices)
        {
            if (string.Equals(notice.PropertyName, propertyName, StringComparison.Ordinal)
                && IsUnset(notice.When, value))
            {
                return notice.Consequence;
            }
        }

        return null;
    }

    private static bool IsUnset(UnsetCondition condition, object? value) => condition switch
    {
        UnsetCondition.Null => value is null,
        UnsetCondition.EmptyText => value is null || (value is string text && string.IsNullOrWhiteSpace(text)),
        UnsetCondition.EmptyList => value is null
                                    || (value is IEnumerable sequence and not string && !HasAny(sequence)),
        UnsetCondition.ZeroOrNegative => value switch
        {
            null => true,
            TimeSpan duration => duration <= TimeSpan.Zero,
            string => false,
            IConvertible number => number.ToDecimal(CultureInfo.InvariantCulture) <= 0,
            _ => false
        },
        _ => false
    };

    private static bool HasAny(IEnumerable sequence)
    {
        foreach (var _ in sequence)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// The entry assembly's informational version, trimmed of the source-revision suffix the SDK
    /// appends. Null when the assembly carries none, which is the case for a plain local build.
    /// </summary>
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
