namespace Basket.API.Features.DeleteBasket;

using BuildingBlocks.CQRS;

// Request DTO
public sealed record DeleteBasketRequest(string UserName);

// Response DTO
public sealed record DeleteBasketResponse(bool IsDeleted);

// Command
public sealed record DeleteBasketCommand(string UserName) : ICommand<DeleteBasketResponse>;