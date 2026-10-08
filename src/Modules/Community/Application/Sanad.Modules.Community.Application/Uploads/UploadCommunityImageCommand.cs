using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Uploads;

public sealed record UploadCommunityImageCommand(
    UserId UploadedBy,
    Stream Content,
    string ContentType,
    long Length) : ICommand<CommunityImageUploadResponse>;

public sealed record CommunityImageUploadResponse(
    Guid ImageId,
    string ImageUrl,
    string ContentType,
    long SizeBytes);

public static class CommunityImageErrors
{
    public static readonly Error Invalid =
        new(
            "Community.Image.Invalid",
            "The uploaded image is invalid.");
}
