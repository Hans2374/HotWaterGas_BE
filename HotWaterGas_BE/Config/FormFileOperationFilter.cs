using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HotWaterGas_BE.Config;

/// <summary>
/// Tells Swagger that IFormFile parameters are file uploads so the UI renders
/// a file-picker widget and the OpenAPI schema is generated correctly.
/// Required when an action uses [FromForm] IFormFile.
/// </summary>
public class FormFileOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var formFileParameters = context.ApiDescription
            .ActionDescriptor
            .Parameters
            .Where(p => p.ParameterType == typeof(IFormFile))
            .ToList();

        if (!formFileParameters.Any())
        {
            return;
        }

        // Remove auto-generated parameters — Swagger can't infer the schema for IFormFile
        operation.Parameters.Clear();

        foreach (var _ in formFileParameters)
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["file"] = new OpenApiSchema
                                {
                                    Type = "string",
                                    Format = "binary",
                                    Description = "The image file to upload (PNG, JPEG, or WebP)."
                                }
                            },
                            Required = new HashSet<string> { "file" }
                        }
                    }
                }
            };
        }
    }
}
