// src/Services/Catalog/Catalog.API/Features/UpdateProduct/UpdateProductHandler.cs

using BuildingBlocks.CQRS;
using BuildingBlocks.Results;
using Catalog.API.Models;
using Marten;

namespace Catalog.API.Features.UpdateProduct;

// --- Request & Response ---
public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    List<string> Categories,
    string Description,
    string ImageFile,
    decimal Price) : ICommand<UpdateProductResult>;

public sealed record UpdateProductResult(bool IsSuccess);

// --- Handler ---
internal sealed class UpdateProductHandler(IDocumentSession session)
    : ICommandHandler<UpdateProductCommand, UpdateProductResult>
{
    public async Task<Result<UpdateProductResult>> Handle(
        UpdateProductCommand command, CancellationToken cancellationToken)
    {
        // 1. Mevcut ürünü bul
        var product = await session.LoadAsync<Product>(command.Id, cancellationToken);

        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product with id '{command.Id}' not found.");

        // 2. Alanları güncelle
        product.Name = command.Name;
        product.Categories = command.Categories;
        product.Description = command.Description;
        product.ImageFile = command.ImageFile;
        product.Price = command.Price;

        // 3. Marten'a güncellemeyi bildir + kaydet
        session.Update(product);
        await session.SaveChangesAsync(cancellationToken);

        return new UpdateProductResult(true);
    }
}