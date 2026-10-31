using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Application.HelpRequests;
public sealed record AcceptCaregiverHelpRequestCommand(UserId UserId, Guid RequestId) : ICommand<CaregiverHelpRequestResponse>;