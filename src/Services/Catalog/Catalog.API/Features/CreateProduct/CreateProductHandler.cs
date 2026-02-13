// src/Services/Catalog/Catalog.API/Features/CreateProduct/CreateProductHandler.cs

using BuildingBlocks.CQRS;
using BuildingBlocks.Results;
using Catalog.API.Models;
using Marten;

namespace Catalog.API.Features.CreateProduct;

// --- Request & Response ---
public sealed record CreateProductCommand(
    string Name,
    List<string> Categories,
    string Description,
    string ImageFile,
    decimal Price) : ICommand<CreateProductResult>;

public sealed record CreateProductResult(Guid Id);

// --- Handler ---
internal sealed class CreateProductHandler(IDocumentSession session)
    : ICommandHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<Result<CreateProductResult>> Handle(
        CreateProductCommand command, CancellationToken cancellationToken)
    {
        // 1. Command → Entity mapping
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Categories = command.Categories,
            Description = command.Description,
            ImageFile = command.ImageFile,
            Price = command.Price
        };

        // 2. Marten'a kaydet
        session.Store(product);
        await session.SaveChangesAsync(cancellationToken);

        // 3. Oluşturulan ID'yi dön
        return new CreateProductResult(product.Id);
    }
}