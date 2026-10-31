using MediatR;
using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.BuildingBlocks.Application.CQRS;

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;