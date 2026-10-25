using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sanad.API.Idempotency;

namespace Sanad.UnitTests.API;

/// <summary>
/// The replay snapshot must be written in the same representation MVC uses for
/// the live response. MVC emits raw UTF-8 for non-ASCII text; a plain
/// <c>JsonSerializer</c> call on the unmodified MVC options would escape it as
/// <c>\uXXXX</c> and break byte-identical replay (observed on the rehearsal
/// release, 2026-10-25).
/// </summary>
public sealed class IdempotencySnapshotterTests
{
    private static MvcIdempotentResponseSnapshotter NewSnapshotter() =>
        new(Options.Create(new JsonOptions()));

    [Fact]
    public void Snapshot_Keeps_Non_Ascii_Text_Raw_Like_The_Mvc_Formatter()
    {
        string body = NewSnapshotter().Snapshot(new
        {
            hospitalName = "مستشفى النور",
            details = "زيارة متابعة"
        });

        Assert.Contains("مستشفى النور", body);
        Assert.Contains("زيارة متابعة", body);
        Assert.DoesNotContain("\\u", body);
    }

    [Fact]
    public void Snapshot_Is_Stable_For_The_Same_Value()
    {
        var value = new { id = Guid.Parse("01a1731a-0d3b-7cd1-9cbb-924516c6c0c5"), note = "ملاحظة" };
        MvcIdempotentResponseSnapshotter snapshotter = NewSnapshotter();

        Assert.Equal(snapshotter.Snapshot(value), snapshotter.Snapshot(value));
    }
}
