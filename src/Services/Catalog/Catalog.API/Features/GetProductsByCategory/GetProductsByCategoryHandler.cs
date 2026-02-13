using BuildingBlocks.CQRS;
using BuildingBlocks.Results;
using Catalog.API.Models;
using Marten;

namespace Catalog.API.Products.GetProductsByCategory;

public sealed record GetProductsByCategoryQuery(string Category) : IQuery<GetProductsByCategoryResult>;

public sealed record GetProductsByCategoryResult(IReadOnlyList<Product> Products);

internal sealed class GetProductsByCategoryHandler(IDocumentSession session)
    : IQueryHandler<GetProductsByCategoryQuery, GetProductsByCategoryResult>
{
    public async Task<Result<GetProductsByCategoryResult>> Handle(
        GetProductsByCategoryQuery query,
        CancellationToken cancellationToken)
    {
        // Marten LINQ — Categories array'inde case-insensitive arama
        var products = await session.Query<Product>()
            .Where(p => p.Categories.Contains(query.Category))
            .ToListAsync(cancellationToken);

        return new GetProductsByCategoryResult(products);
    }
}