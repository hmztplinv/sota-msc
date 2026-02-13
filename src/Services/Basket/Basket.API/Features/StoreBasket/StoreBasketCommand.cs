namespace Basket.API.Features.StoreBasket;

using Basket.API.Models;
using BuildingBlocks.CQRS;

// Request DTO — API'den gelen body
public sealed record StoreBasketRequest(ShoppingCart Cart);

// Response DTO
public sealed record StoreBasketResponse(string UserName);

// Command — handler'a gider
public sealed record StoreBasketCommand(ShoppingCart Cart) : ICommand<StoreBasketResponse>;