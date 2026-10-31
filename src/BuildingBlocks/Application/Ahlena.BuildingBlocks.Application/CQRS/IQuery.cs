using MediatR;
using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.BuildingBlocks.Application.CQRS;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;