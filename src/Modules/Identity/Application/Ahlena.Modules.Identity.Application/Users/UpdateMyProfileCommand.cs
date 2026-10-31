using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed record UpdateMyProfileCommand(
    UserId CurrentUserId,
    string? ArabicFullName,
    string? EnglishFullName,
    string? Email,
    string? PhoneNumber)
    : ICommand<UpdateMyProfileResponse>;
