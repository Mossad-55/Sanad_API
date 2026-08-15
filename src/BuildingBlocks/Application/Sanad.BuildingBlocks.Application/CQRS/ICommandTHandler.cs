using MediatR;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.BuildingBlocks.Application.CQRS;

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;