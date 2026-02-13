namespace Basket.API.Features.GetBasket;

using Basket.API.Data;
using Basket.API.Models;
using BuildingBlocks.CQRS;
using BuildingBlocks.Results;

public sealed class GetBasketHandler(IBasketRepository repository)
    : IQueryHandler<GetBasketQuery, ShoppingCart>
{
    public async Task<Result<ShoppingCart>> Handle(GetBasketQuery query, CancellationToken cancellationToken)
    {
        var basket = await repository.GetBasketAsync(query.UserName, cancellationToken);

        // Sepet bulunamazsa boş sepet dön — null yerine empty object
        // Bu bir "design decision": 404 yerine boş sepet dönmek UX açısından daha iyi
        return Result<ShoppingCart>.Success(basket ?? new ShoppingCart(query.UserName));
    }
}