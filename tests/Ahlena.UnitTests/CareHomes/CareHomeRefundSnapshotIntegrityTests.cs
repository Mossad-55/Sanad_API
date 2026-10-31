using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// Stored-snapshot integrity, fail closed (review item M4): a persisted
/// revision-2 snapshot that cannot be re-read — out-of-band corruption the
/// aggregate refuses at creation, so only a reflection-set field can represent
/// it — fails the aggregate's money decisions with its own loud exception and
/// the family read with a stable <c>CareHomes.Bookings.UnreadableSnapshot</c>
/// code, never a 500 and never a silently terms-less response. The unknown
/// persisted revision value is pinned by the policy facts
/// (<c>Persisted_versions_reject_unknown_revisions…</c>).
/// </summary>
public sealed class CareHomeRefundSnapshotIntegrityTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private static CareHomeBooking NewV2Booking()
    {
        CareHomeBooking booking = CareHomeBooking.Create(
            new CareHomeId(Guid.NewGuid()), UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 12000m, 600m, 630m, 1, "ع", "Elderly", 75, null, "Contact", null, null, null, Now,
            policySnapshot: CareHomePolicySnapshot.From(CareHomeRefundPolicyVersion.ForNewBooking(true), null, null));
        booking.MarkPaid(9001, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        return booking;
    }

    private static void CorruptSnapshot(CareHomeBooking booking) =>
        typeof(CareHomeBooking).GetProperty(nameof(CareHomeBooking.CancellationTermsSnapshotJson))!
            .SetValue(booking, "{corrupted");

    [Fact]
    public void A_corrupt_persisted_snapshot_fails_the_money_decision_closed()
    {
        CareHomeBooking booking = NewV2Booking();
        CorruptSnapshot(booking);

        // The aggregate refuses to decide money from data it cannot honestly
        // re-read — the exception handlers in the application layer translate
        // this into the stable unreadable-snapshot code.
        Assert.Throws<DomainException>(() => booking.CancelByFamily(Now.AddMinutes(5)));
    }

    [Fact]
    public async Task A_corrupt_persisted_snapshot_fails_the_family_read_with_a_stable_code()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        CareHomeBooking booking = NewV2Booking();
        CorruptSnapshot(booking);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await new FamilyDetailHandler(db).Handle(
            new GetFamilyCareHomeBookingQuery(UserId.New(), booking.FamilyId, booking.Id, Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Bookings.UnreadableSnapshot", result.Error!.Code);
    }
}
