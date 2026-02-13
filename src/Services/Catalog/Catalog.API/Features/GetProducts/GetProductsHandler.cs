// src/Services/Catalog/Catalog.API/Features/GetProducts/GetProductsHandler.cs

using BuildingBlocks.CQRS;
using BuildingBlocks.Results;
using Catalog.API.Models;
using Marten;
using Marten.Pagination;

namespace Catalog.API.Features.GetProducts;

// --- Request & Response ---
public sealed record GetProductsQuery(int PageNumber = 1, int PageSize = 10) 
    : IQuery<GetProductsResult>;

public sealed record GetProductsResult(
    IEnumerable<Product> Products,
    long TotalCount,
    int PageNumber,
    int PageSize);

// --- Handler ---
internal sealed class GetProductsHandler(IDocumentSession session)
    : IQueryHandler<GetProductsQuery, GetProductsResult>
{
    public async Task<Result<GetProductsResult>> Handle(
        GetProductsQuery query, CancellationToken cancellationToken)
    {
        var products = await session.Query<Product>()
            .ToPagedListAsync(query.PageNumber, query.PageSize, cancellationToken);

        return new GetProductsResult(
            products,
            products.TotalItemCount,
            query.PageNumber,
            query.PageSize);
    }
}