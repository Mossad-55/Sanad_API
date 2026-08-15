using MediatR;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.BuildingBlocks.Application.CQRS;

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;