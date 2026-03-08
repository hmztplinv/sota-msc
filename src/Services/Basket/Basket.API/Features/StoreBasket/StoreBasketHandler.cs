namespace Basket.API.Features.StoreBasket;

using Basket.API.Data;
using Basket.API.Models;
using BuildingBlocks.CQRS;
using BuildingBlocks.Results;
using Discount.Grpc;

public sealed class StoreBasketHandler(
    IBasketRepository repository,
    DiscountProtoService.DiscountProtoServiceClient discountClient)
    : ICommandHandler<StoreBasketCommand, StoreBasketResponse>
{
    public async Task<Result<StoreBasketResponse>> Handle(
        StoreBasketCommand command, CancellationToken cancellationToken)
    {
        // 1. Her sepet kalemi için Discount servisinden indirim sorgula
        await ApplyDiscountsAsync(command.Cart.Items, cancellationToken);

        // 2. İndirimli sepeti kaydet (CachedBasketRepository → BasketRepository)
        var basket = await repository.StoreBasketAsync(command.Cart, cancellationToken);

        return Result<StoreBasketResponse>.Success(new StoreBasketResponse(basket.UserName));
    }

    /// <summary>
    /// Her sepet kalemine Discount.Grpc üzerinden indirim uygular.
    /// İndirim yoksa Amount=0 döner, fiyat değişmez.
    /// </summary>
    private async Task ApplyDiscountsAsync(
        List<ShoppingCartItem> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            var discount = await discountClient.GetDiscountAsync(
                new GetDiscountRequest { ProductName = item.ProductName },
                cancellationToken: cancellationToken);

            // Negatif fiyat koruması
            item.Price -= discount.Amount;
            if (item.Price < 0) item.Price = 0;
        }
    }
}
