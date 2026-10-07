using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Application.HelpRequests;
public sealed record AcknowledgeFamilyDependentHelpRequestCommand(UserId UserId, ElderlyId DependentId, Guid RequestId) : ICommand<ElderlyHelpRequestResponse>;