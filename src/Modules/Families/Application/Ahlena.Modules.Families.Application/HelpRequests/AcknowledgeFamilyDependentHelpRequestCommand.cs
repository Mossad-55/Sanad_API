using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Application.HelpRequests;
public sealed record AcknowledgeFamilyDependentHelpRequestCommand(UserId UserId, ElderlyId DependentId, Guid RequestId) : ICommand<ElderlyHelpRequestResponse>;