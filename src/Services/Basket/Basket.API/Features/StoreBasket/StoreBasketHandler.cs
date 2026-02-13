namespace Basket.API.Features.StoreBasket;

using Basket.API.Data;
using BuildingBlocks.CQRS;
using BuildingBlocks.Results;

public sealed class StoreBasketHandler(IBasketRepository repository)
    : ICommandHandler<StoreBasketCommand, StoreBasketResponse>
{
    public async Task<Result<StoreBasketResponse>> Handle(StoreBasketCommand command, CancellationToken cancellationToken)
    {
        var basket = await repository.StoreBasketAsync(command.Cart, cancellationToken);

        return Result<StoreBasketResponse>.Success(new StoreBasketResponse(basket.UserName));
    }
}