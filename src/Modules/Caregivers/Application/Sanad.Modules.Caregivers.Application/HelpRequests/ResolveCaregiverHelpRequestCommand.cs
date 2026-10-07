using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Application.HelpRequests;
public sealed record ResolveCaregiverHelpRequestCommand(UserId UserId, Guid RequestId, string Reason) : ICommand<CaregiverHelpRequestResponse>;
