using System.Reflection;
using AwesomeAssertions;
using Data.Configuration;
using Ingestor.Configuration;
using TrueMain.Configuration;

namespace TrueMain.UnitTests;

/// <summary>
/// Guards the effective-configuration allow-list (#1034): every section either host's
/// catalog declares is exposed on the admin configuration page, so the one thing that
/// must never happen is a section quietly carrying a credential-shaped property.
///
/// <para>
/// <see cref="EffectiveConfigurationBuilder"/> already drops a secret-named property at
/// build time as a backstop, but that is silent — a mis-authored catalog entry would
/// simply omit the value instead of failing anything. This test is what actually enforces
/// the allow-list: it walks the real production catalogs, so adding a new section (or
/// widening an existing one's <c>IncludeProperties</c>) to something secret-shaped fails
/// here rather than shipping.
/// </para>
/// </summary>
public sealed class EffectiveConfigurationCatalogTests
{
    public static IEnumerable<object[]> Catalogs()
    {
        yield return [IngestorEffectiveConfigurationCatalog.Instance];
        yield return [ApiEffectiveConfigurationCatalog.Instance];
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void NoExposedPropertyLooksLikeASecret(EffectiveConfigurationCatalog catalog)
    {
        foreach (var section in catalog.Sections)
        {
            var exposed = ExposedPropertyNames(section).ToList();

            exposed.Should().NotBeEmpty(
                $"{catalog.ProcessName}:{section.SectionName} declares a section that exposes nothing");

            exposed.Where(EffectiveConfigurationRedaction.IsSecretName).Should().BeEmpty(
                $"{catalog.ProcessName}:{section.SectionName} would expose a credential-shaped property");
        }
    }

    /// <summary>
    /// Catches a typo'd or renamed entry in <see cref="EffectiveConfigurationSectionDescriptor.IncludeProperties"/>:
    /// such an entry silently exposes nothing (<c>EffectiveConfigurationBuilder</c> only
    /// ever narrows the real property list), which would pass the test above for the
    /// wrong reason.
    /// </summary>
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void EveryIncludedPropertyNameExistsOnTheOptionsType(EffectiveConfigurationCatalog catalog)
    {
        foreach (var section in catalog.Sections)
        {
            if (section.IncludeProperties is null)
            {
                continue;
            }

            var actual = ReadableProperties(section.OptionsType).Select(property => property.Name).ToHashSet();

            foreach (var name in section.IncludeProperties)
            {
                actual.Should().Contain(
                    name,
                    $"{catalog.ProcessName}:{section.SectionName} lists IncludeProperties " +
                    $"\"{name}\", which does not exist on {section.OptionsType.Name}");
            }
        }
    }

    private static IEnumerable<string> ExposedPropertyNames(EffectiveConfigurationSectionDescriptor section)
    {
        var names = ReadableProperties(section.OptionsType).Select(property => property.Name);

        return section.IncludeProperties is null
            ? names
            : names.Where(name => section.IncludeProperties.Contains(name));
    }

    private static IEnumerable<PropertyInfo> ReadableProperties(Type optionsType) =>
        optionsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
}
