using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Uploads;

public static class CommunityImageUrls
{
    public static string For(CommunityImageId imageId) =>
        "/api/v1/community/uploads/images/" + imageId.Value + "/file";
}
