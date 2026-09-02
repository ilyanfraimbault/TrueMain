using System.Reflection;
using AwesomeAssertions;
using Data.Ops.Mongo;
using MongoDB.Bson.Serialization.Attributes;

namespace TrueMain.UnitTests;

/// <summary>
/// Every persisted field of a Mongo document declares its own camelCase name.
/// </summary>
/// <remarks>
/// There is no global camelCase convention pack registered, so a property without
/// <see cref="BsonElementAttribute"/> silently persists under its PascalCase C# name. It
/// round-trips fine — the same POCO writes and reads it — so nothing fails; what breaks is
/// everything that reads the collection without this class: a `mongosh` query, a raw
/// migration, an admin panel built against the documented field name. That is exactly how
/// #1362's `jobMode` shipped documented as camelCase and persisted as `JobMode`, caught in
/// review rather than by a test. Hence this one.
/// </remarks>
public sealed class MongoDocumentFieldNamingTests
{
    public static TheoryData<Type> DocumentTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(ProcessRunDocument).Assembly
                     .GetTypes()
                     .Where(type => type is { IsClass: true, IsAbstract: false }
                                    && type.Name.EndsWith("Document", StringComparison.Ordinal))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DocumentTypes))]
    public void EveryPersistedProperty_DeclaresItsCamelCaseFieldName(Type documentType)
    {
        var unmapped = PersistedProperties(documentType)
            .Where(property => property.GetCustomAttribute<BsonElementAttribute>() is null)
            .Select(property => property.Name)
            .ToList();

        unmapped.Should().BeEmpty(
            $"{documentType.Name} persists these under their PascalCase C# name instead of the "
            + "collection's camelCase convention");
    }

    [Theory]
    [MemberData(nameof(DocumentTypes))]
    public void EveryDeclaredFieldName_IsCamelCase(Type documentType)
    {
        var wrongCase = PersistedProperties(documentType)
            .Select(property => property.GetCustomAttribute<BsonElementAttribute>()?.ElementName)
            .OfType<string>()
            .Where(name => !char.IsLower(name[0]))
            .ToList();

        wrongCase.Should().BeEmpty($"{documentType.Name} declares a field name that is not camelCase");
    }

    // The id is mapped by [BsonId] and stored as `_id`, so it neither needs nor should
    // carry an element name; a property the driver ignores has no field to name either.
    private static IEnumerable<PropertyInfo> PersistedProperties(Type documentType)
        => documentType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite
                               && property.GetCustomAttribute<BsonIdAttribute>() is null
                               && property.GetCustomAttribute<BsonIgnoreAttribute>() is null);
}
