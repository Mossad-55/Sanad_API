using MediatR;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.BuildingBlocks.Application.CQRS;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;