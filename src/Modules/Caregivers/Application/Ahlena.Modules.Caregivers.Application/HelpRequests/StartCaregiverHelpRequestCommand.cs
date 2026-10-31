using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Application.HelpRequests;
public sealed record StartCaregiverHelpRequestCommand(UserId UserId, Guid RequestId) : ICommand<CaregiverHelpRequestResponse>;