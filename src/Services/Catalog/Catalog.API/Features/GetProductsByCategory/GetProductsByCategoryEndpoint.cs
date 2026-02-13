using Carter;
using MediatR;

namespace Catalog.API.Products.GetProductsByCategory;

public sealed class GetProductsByCategoryEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/category/{category}",
            async (string category, ISender sender) =>
            {
                var result = await sender.Send(new GetProductsByCategoryQuery(category));

                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : Results.NotFound(result.Error);
            })
            .WithName("GetProductsByCategory")
            .WithTags("Products")
            .Produces<GetProductsByCategoryResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithDescription("Get products filtered by category name");
    }
}