// src/Services/Catalog/Catalog.API/Features/DeleteProduct/DeleteProductEndpoint.cs

using Carter;
using MediatR;

namespace Catalog.API.Features.DeleteProduct;

public sealed class DeleteProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/products/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteProductCommand(id));

            return result.IsSuccess
                ? Results.NoContent()
                : Results.NotFound(result.Error);
        })
        .WithName("DeleteProduct")
        .WithTags("Products")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(404)
        .WithDescription("Delete a product");
    }
}