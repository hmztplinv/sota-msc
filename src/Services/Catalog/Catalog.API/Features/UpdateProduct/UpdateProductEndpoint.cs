// src/Services/Catalog/Catalog.API/Features/UpdateProduct/UpdateProductEndpoint.cs

using Carter;
using MediatR;

namespace Catalog.API.Features.UpdateProduct;

// --- API Request DTO ---
public sealed record UpdateProductRequest(
    string Name,
    List<string> Categories,
    string Description,
    string ImageFile,
    decimal Price);

public sealed class UpdateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/products/{id:guid}", async (
            Guid id,
            UpdateProductRequest request,
            ISender sender) =>
        {
            var command = new UpdateProductCommand(
                id,
                request.Name,
                request.Categories,
                request.Description,
                request.ImageFile,
                request.Price);

            var result = await sender.Send(command);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(result.Error);
        })
        .WithName("UpdateProduct")
        .WithTags("Products")
        .Produces<UpdateProductResult>()
        .ProducesProblem(404)
        .WithDescription("Update an existing product");
    }
}