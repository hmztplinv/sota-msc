// src/Services/Catalog/Catalog.API/Features/UpdateProduct/UpdateProductEndpoint.cs

using Carter;
using Mapster;
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
            var command = request.Adapt<UpdateProductCommand>() with { Id = id };


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