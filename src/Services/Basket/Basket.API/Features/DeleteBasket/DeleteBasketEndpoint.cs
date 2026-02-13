namespace Basket.API.Features.DeleteBasket;

using BuildingBlocks.Results;
using Carter;
using MediatR;

public sealed class DeleteBasketEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/basket/{userName}", async (string userName, ISender sender) =>
        {
            var result = await sender.Send(new DeleteBasketCommand(userName));

            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(result.Error);
        })
        .WithName("DeleteBasket")
        .WithTags("Basket")
        .Produces<DeleteBasketResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithDescription("Kullanıcının sepetini siler.");
    }
}