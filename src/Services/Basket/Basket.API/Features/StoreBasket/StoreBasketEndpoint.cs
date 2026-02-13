namespace Basket.API.Features.StoreBasket;

using BuildingBlocks.Results;
using Carter;
using Mapster;
using MediatR;

public sealed class StoreBasketEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/basket", async (StoreBasketRequest request, ISender sender) =>
        {
            var command = request.Adapt<StoreBasketCommand>();

            var result = await sender.Send(command);

            return result.IsSuccess
                ? Results.Created($"/basket/{result.Value.UserName}", result.Value)
                : Results.BadRequest(result.Error);
        })
        .WithName("StoreBasket")
        .WithTags("Basket")
        .Produces<StoreBasketResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithDescription("Sepeti oluşturur veya günceller.");
    }
}