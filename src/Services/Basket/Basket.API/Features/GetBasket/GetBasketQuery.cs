namespace Basket.API.Features.GetBasket;

using Basket.API.Models;
using BuildingBlocks.CQRS;

// REPR: Request → tek bir record
public sealed record GetBasketQuery(string UserName) : IQuery<ShoppingCart>;