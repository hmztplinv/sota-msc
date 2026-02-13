// src/Services/Catalog/Catalog.API/Features/DeleteProduct/DeleteProductHandler.cs

using BuildingBlocks.CQRS;
using BuildingBlocks.Results;
using Catalog.API.Models;
using Marten;

namespace Catalog.API.Features.DeleteProduct;

// --- Request ---
public sealed record DeleteProductCommand(Guid Id) : ICommand;

// --- Handler ---
internal sealed class DeleteProductHandler(IDocumentSession session)
    : ICommandHandler<DeleteProductCommand>
{
    public async Task<Result> Handle(
        DeleteProductCommand command, CancellationToken cancellationToken)
    {
        var product = await session.LoadAsync<Product>(command.Id, cancellationToken);

        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product with id '{command.Id}' not found.");

        session.Delete(product);
        await session.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}