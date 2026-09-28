using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TuniPlan.Infrastructure;

/// <summary>
/// Every enum is declared as a string with all its values (the API serializes enums as strings),
/// and its description lists the meaning of each value when members carry a [Description].
/// </summary>
internal sealed class EnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (!type.IsEnum) return Task.CompletedTask;

        var names = Enum.GetNames(type);
        var isFlags = type.IsDefined(typeof(FlagsAttribute));
        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        // A flags value can combine names ("Client, Business"), so it cannot be a closed list
        schema.Enum = isFlags ? null : names.Select(n => (JsonNode)JsonValue.Create(n)).ToList();

        var lines = names.Select(n =>
        {
            var description = type.GetField(n)?.GetCustomAttribute<DescriptionAttribute>()?.Description;
            return description is null ? $"- `{n}`" : $"- `{n}`: {description}";
        });
        var header = type.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var flags = isFlags ? "Flags: one or several values separated by \", \".\n\n" : "";
        schema.Description = $"{(header is null ? "" : header + "\n\n")}{flags}{string.Join("\n", lines)}";
        return Task.CompletedTask;
    }
}
