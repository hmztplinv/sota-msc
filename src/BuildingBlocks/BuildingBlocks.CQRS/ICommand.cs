using BuildingBlocks.Results;
using MediatR;

namespace BuildingBlocks.CQRS;

/// <summary>
/// Değer döndüren command. Örnek: CreateProduct → Result{Guid}
/// IRequest{Result{TResponse}} — MediatR pipeline'ına girer.
/// Result{T} sarmalıyoruz çünkü her command başarısız olabilir.
/// </summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>
/// Değer döndürmeyen command. Örnek: DeleteProduct → Result
/// </summary>
public interface ICommand : IRequest<Result>;

/// <summary>
/// ICommand{TResponse} handler'ı. 
/// MediatR'ın IRequestHandler'ını wrap ediyoruz — tüm handler'lar Result{T} döner.
/// </summary>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

/// <summary>
/// Değer döndürmeyen command handler'ı.
/// </summary>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;
