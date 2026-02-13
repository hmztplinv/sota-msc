namespace Basket.API.Features.GetBasket;

using Basket.API.Models;
using BuildingBlocks.Results;
using Carter;
using MediatR;

public sealed class GetBasketEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/basket/{userName}", async (string userName, ISender sender) =>
        {
            var result = await sender.Send(new GetBasketQuery(userName));

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.NotFound(result.Error);
        })
        .WithName("GetBasket")
        .WithTags("Basket")
        .Produces<ShoppingCart>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithDescription("Kullanıcının sepetini getirir. Sepet yoksa boş sepet döner.");
    }
}