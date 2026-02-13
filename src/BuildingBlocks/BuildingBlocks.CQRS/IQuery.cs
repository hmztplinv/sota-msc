using BuildingBlocks.Results;
using MediatR;

namespace BuildingBlocks.CQRS;

/// <summary>
/// Query — veri okuma işlemi. Her zaman değer döner.
/// Command'dan ayrı interface: pipeline behavior'larda ayırt edebilmek için.
/// Örnek: GetProducts → Result{IReadOnlyList{ProductResponse}}
/// </summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

/// <summary>
/// Query handler'ı. Her query mutlaka Result{T} döner.
/// </summary>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
