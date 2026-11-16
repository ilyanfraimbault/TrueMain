using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace TrueMain.OpenApi;

public static class OpenApiServiceCollectionExtensions
{
    private const string JsonMediaType = "application/json";

    /// <summary>
    /// The API's wire contract: enums travel as their C# names, and the OpenAPI document describes
    /// what the API writes, so the TypeScript generated from it (<c>web/shared/contract</c>) is exact.
    /// </summary>
    public static IServiceCollection AddTrueMainContract(this IServiceCollection services)
    {
        // MVC writes the responses; the OpenAPI generator describes them from the HTTP options.
        services.Configure<MvcJsonOptions>(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddEndpointsApiExplorer();
        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer((schema, _, _) =>
            {
                NumbersAreNumbers(schema);
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer((document, _, _) =>
            {
                JsonOnly(document);
                ResponseFieldsAreRequired(document);
                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>
    /// The web JSON defaults read numbers from strings too, so the generated schema says
    /// <c>integer | string</c>; the API only ever writes numbers.
    /// </summary>
    private static void NumbersAreNumbers(OpenApiSchema schema)
    {
        if (schema.Type is not { } type
            || !type.HasFlag(JsonSchemaType.String)
            || (!type.HasFlag(JsonSchemaType.Integer) && !type.HasFlag(JsonSchemaType.Number)))
        {
            return;
        }

        schema.Type = type & ~JsonSchemaType.String;
        schema.Pattern = null;
    }

    /// <summary>
    /// MVC lists every media type its formatters could write (<c>text/plain</c>, <c>text/json</c>);
    /// the API speaks <c>application/json</c>.
    /// </summary>
    private static void JsonOnly(OpenApiDocument document)
    {
        var operations = document.Paths.Values.SelectMany(path => path.Operations?.Values.AsEnumerable() ?? []);
        foreach (var operation in operations)
        {
            var contents = (operation.Responses?.Values.Select(response => response.Content) ?? [])
                .Append(operation.RequestBody?.Content);
            foreach (var content in contents)
            {
                if (content is null || !content.ContainsKey(JsonMediaType))
                {
                    continue;
                }

                foreach (var mediaType in content.Keys.Where(key => key != JsonMediaType).ToList())
                {
                    content.Remove(mediaType);
                }
            }
        }
    }

    /// <summary>
    /// The API writes every property, null or not, so every property of a response schema is
    /// required. Schemas reachable from a request body keep the C# rule (only <c>required</c>
    /// members), since a client may leave the others out.
    /// </summary>
    private static void ResponseFieldsAreRequired(OpenApiDocument document)
    {
        if (document.Components?.Schemas is not { } schemas)
        {
            return;
        }

        var requestSchemas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var body in RequestBodies(document))
        {
            CollectReferences(body, schemas, requestSchemas);
        }

        foreach (var (id, schema) in schemas)
        {
            if (requestSchemas.Contains(id) || schema is not OpenApiSchema { Properties.Count: > 0 } concrete)
            {
                continue;
            }

            concrete.Required ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in concrete.Properties.Keys)
            {
                concrete.Required.Add(property);
            }
        }
    }

    private static IEnumerable<IOpenApiSchema> RequestBodies(OpenApiDocument document) =>
        document.Paths.Values
            .SelectMany(path => path.Operations?.Values.AsEnumerable() ?? [])
            .SelectMany(operation => operation.RequestBody?.Content?.Values.AsEnumerable() ?? [])
            .Select(media => media.Schema)
            .OfType<IOpenApiSchema>();

    private static void CollectReferences(
        IOpenApiSchema? schema,
        IDictionary<string, IOpenApiSchema> components,
        HashSet<string> seen)
    {
        if (schema is null)
        {
            return;
        }

        if (schema is OpenApiSchemaReference { Reference.Id: { } id })
        {
            if (seen.Add(id) && components.TryGetValue(id, out var target))
            {
                CollectReferences(target, components, seen);
            }

            return;
        }

        CollectReferences(schema.Items, components, seen);
        CollectReferences(schema.AdditionalProperties, components, seen);
        foreach (var child in (schema.Properties?.Values ?? [])
                     .Concat(schema.AllOf ?? [])
                     .Concat(schema.AnyOf ?? [])
                     .Concat(schema.OneOf ?? []))
        {
            CollectReferences(child, components, seen);
        }
    }
}
