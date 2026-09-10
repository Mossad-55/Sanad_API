using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record UpdateMyProfileCommand(
    UserId CurrentUserId,
    string? ArabicFullName,
    string? EnglishFullName,
    string? Email,
    string? PhoneNumber)
    : ICommand<UpdateMyProfileResponse>;
