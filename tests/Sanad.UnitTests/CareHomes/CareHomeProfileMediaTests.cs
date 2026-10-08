using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeProfileMediaTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RevisionId = Guid.Parse("0199eaf3-99b0-7c41-9490-6f77f8298d01");

    [Fact]
    public void Create_accepts_private_jpg_and_png_within_size_limit()
    {
        var cover = CareHomeProfileMedia.Create(RevisionId, CareHomeProfileMediaKind.Cover, 0,
            "private/care-home-media/cover.jpg", "image/jpeg", 5_242_880, UtcNow);
        var gallery = CareHomeProfileMedia.Create(RevisionId, CareHomeProfileMediaKind.Gallery, 9,
            "private/care-home-media/photo.png", "image/png", 1, UtcNow);

        Assert.Equal(CareHomeProfileMediaKind.Cover, cover.Kind);
        Assert.Equal(9, gallery.Position);
    }

    [Theory]
    [InlineData("care-home-media/photo.jpg", "image/jpeg", 100)]
    [InlineData("private/care-home-media/photo.gif", "image/gif", 100)]
    [InlineData("private/care-home-media/photo.jpg", "image/jpeg", 5_242_881)]
    public void Create_rejects_nonprivate_or_unsupported_or_oversized_images(string key, string type, long length)
    {
        Assert.Throws<DomainException>(() => CareHomeProfileMedia.Create(RevisionId,
            CareHomeProfileMediaKind.Gallery, 0, key, type, length, UtcNow));
    }
}
