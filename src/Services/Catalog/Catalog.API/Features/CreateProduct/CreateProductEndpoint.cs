// src/Services/Catalog/Catalog.API/Features/CreateProduct/CreateProductEndpoint.cs

using Carter;
using MediatR;

namespace Catalog.API.Features.CreateProduct;

// --- API Request DTO ---
public sealed record CreateProductRequest(
    string Name,
    List<string> Categories,
    string Description,
    string ImageFile,
    decimal Price);

public sealed class CreateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/products", async (CreateProductRequest request, ISender sender) =>
        {
            var command = new CreateProductCommand(
                request.Name,
                request.Categories,
                request.Description,
                request.ImageFile,
                request.Price);

            var result = await sender.Send(command);

            return result.IsSuccess
                ? Results.Created($"/api/products/{result.Value.Id}", result.Value)
                : Results.Problem(title: result.Error.Message, statusCode: 400);
        })
        .WithName("CreateProduct")
        .WithTags("Products")
        .Produces<CreateProductResult>(StatusCodes.Status201Created)
        .ProducesProblem(400)
        .WithDescription("Create a new product");
    }
}