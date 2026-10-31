using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Community.Application.Uploads;

public static class CommunityImageUrls
{
    private const string Prefix = "/api/v1/community/uploads/images/";
    private const string Suffix = "/file";

    public static string For(CommunityImageId imageId) =>
        Prefix + imageId.Value + Suffix;

    /// <summary>
    /// Parses the authorized Community image read URL back to its image id.
    /// Anything else — an external URL, a different route family, a key path
    /// or a malformed id — is not an upload reference, so the authoring flows
    /// (create and edit) reject it instead of persisting an unverifiable
    /// asset pointer.
    /// </summary>
    public static bool TryParseImageId(string? url, out CommunityImageId imageId)
    {
        imageId = default;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        string value = url.Trim();
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) ||
            !value.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }

        string raw = value[Prefix.Length..^Suffix.Length];
        if (!Guid.TryParseExact(raw, "D", out Guid id))
        {
            return false;
        }

        imageId = new CommunityImageId(id);
        return true;
    }
}
