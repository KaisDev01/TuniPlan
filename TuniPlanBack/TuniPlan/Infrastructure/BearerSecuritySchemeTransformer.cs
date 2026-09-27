using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TuniPlan.Infrastructure;

/// <summary>Adds the JWT "Bearer" scheme to the OpenAPI document (for the Scalar UI "Authorize" button).</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Access token returned by /api/auth/login (valid 15 minutes)."
        };

        foreach (var operation in document.Paths.Values.Where(p => p.Operations is not null).SelectMany(p => p.Operations!))
        {
            operation.Value.Security ??= [];
            operation.Value.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        }
        return Task.CompletedTask;
    }
}
