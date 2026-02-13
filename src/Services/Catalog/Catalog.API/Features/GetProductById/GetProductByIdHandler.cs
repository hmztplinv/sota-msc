// src/Services/Catalog/Catalog.API/Features/GetProductById/GetProductByIdHandler.cs

using BuildingBlocks.CQRS;
using BuildingBlocks.Results;
using Catalog.API.Models;
using Marten;

namespace Catalog.API.Features.GetProductById;

// --- Request & Response ---
public sealed record GetProductByIdQuery(Guid Id) : IQuery<GetProductByIdResult>;

public sealed record GetProductByIdResult(Product Product);

// --- Handler ---
internal sealed class GetProductByIdHandler(IDocumentSession session)
    : IQueryHandler<GetProductByIdQuery, GetProductByIdResult>
{
    public async Task<Result<GetProductByIdResult>> Handle(
        GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        var product = await session.LoadAsync<Product>(query.Id, cancellationToken);

        if (product is null)
            return Error.NotFound("Product.NotFound", $"Product with id '{query.Id}' not found.");

        return new GetProductByIdResult(product);
    }
}