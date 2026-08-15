using MediatR;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.BuildingBlocks.Application.CQRS;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>;