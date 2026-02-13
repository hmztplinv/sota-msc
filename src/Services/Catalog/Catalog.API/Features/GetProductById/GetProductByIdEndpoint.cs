// src/Services/Catalog/Catalog.API/Features/GetProductById/GetProductByIdEndpoint.cs

using Carter;
using MediatR;

namespace Catalog.API.Features.GetProductById;

public sealed class GetProductByIdEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetProductByIdQuery(id));

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(result.Error);
        })
        .WithName("GetProductById")
        .WithTags("Products")
        .Produces<GetProductByIdResult>()
        .ProducesProblem(404)
        .WithDescription("Get product by ID");
    }
}