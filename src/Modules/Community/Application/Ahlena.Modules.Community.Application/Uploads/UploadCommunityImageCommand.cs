using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Community.Application.Uploads;

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
