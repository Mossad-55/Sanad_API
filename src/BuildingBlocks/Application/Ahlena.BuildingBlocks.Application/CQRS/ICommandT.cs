using MediatR;
using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.BuildingBlocks.Application.CQRS;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>;