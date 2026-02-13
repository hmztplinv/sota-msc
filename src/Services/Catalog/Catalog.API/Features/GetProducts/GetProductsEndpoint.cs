// src/Services/Catalog/Catalog.API/Features/GetProducts/GetProductsEndpoint.cs

using Carter;
using MediatR;

namespace Catalog.API.Features.GetProducts;

public sealed class GetProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", async (
            int? pageNumber,
            int? pageSize,
            ISender sender) =>
        {
            var query = new GetProductsQuery(pageNumber ?? 1, pageSize ?? 10);
            var result = await sender.Send(query);

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error.Message, statusCode: 400);
        })
        .WithName("GetProducts")
        .WithTags("Products")
        .Produces<GetProductsResult>()
        .ProducesProblem(400)
        .WithDescription("Paginated product listing");
    }
}