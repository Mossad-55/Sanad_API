using MediatR;
using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.BuildingBlocks.Application.CQRS;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;