using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MatchForecast.Api.Swagger;

/// <summary>
/// [AllowAnonymous] olmayan her operasyona Bearer gereksinimi ekler; Swagger UI'da kilit ikonu sadece korunan endpoint'lerde görünür
/// ve "Authorize" ile girilen token bu isteklere otomatik eklenir. GetToken kilitsiz kalır.
/// </summary>
public sealed class BearerSecurityOperationFilter : IOperationFilter
{
    public const string SchemeName = "Bearer";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // EndpointMetadata hem controller hem action seviyesindeki attribute'ları içerir.
        if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = []
            }
        ];

        operation.Responses ??= [];
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized — geçerli Bearer token gerekli." });
    }
}