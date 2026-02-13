namespace Basket.API.Features.DeleteBasket;

using Basket.API.Data;
using BuildingBlocks.CQRS;
using BuildingBlocks.Results;

public sealed class DeleteBasketHandler(IBasketRepository repository)
    : ICommandHandler<DeleteBasketCommand, DeleteBasketResponse>
{
    public async Task<Result<DeleteBasketResponse>> Handle(DeleteBasketCommand command, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteBasketAsync(command.UserName, cancellationToken);

        return Result<DeleteBasketResponse>.Success(new DeleteBasketResponse(deleted));
    }
}