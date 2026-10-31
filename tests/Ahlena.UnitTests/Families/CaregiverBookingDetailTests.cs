using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Reports;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// F6 caregiver booking detail. The read must publish the persisted cancellation
/// facts (actor side, action, reason category, refund entitlement, refund
/// decision reason and a fact-aware refund state) and nothing invented: no status
/// ledger, no event history and no derived timeline, and never the recorded
/// caregiver-incident flag or the free-text reason note. The caregiver-only
/// boundary must stay exactly as it was: a booking that belongs to another
/// caregiver is not found.
/// <para>
/// The caregiver record additions must come from the real stored rows: the
/// visit/medical report history of this booking and the family's booking
/// review. <c>upcomingVisits</c> must stay null — no other or future booking
/// is mapped as a visit while the "upcoming" semantics are undecided.
/// </para>
/// </summary>
public sealed class CaregiverBookingDetailTests
{
    [Fact]
    public async Task Detail_publishes_the_persisted_cancellation_facts_and_a_fact_aware_refund_state()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        UserId actorUserId = UserId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelledOnUtc = now.AddMinutes(2);
        booking.CancelByCaregiver("SECRET_BOOKING_REASON", cancelledOnUtc);
        BookingCancellationFact fact = CreateCaregiverCancelFact(
            booking.Id,
            actorUserId,
            booking.ConfirmedOnUtc!.Value,
            cancelledOnUtc,
            "SECRET_REASON_NOTE");

        dbContext.Bookings.Add(booking);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        BookingDetailResponse response = result.Value;

        // The added values are read from the stored fact; none is derived.
        Assert.Equal(BookingCancellationActorSide.Caregiver, response.CancellationActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, response.CancellationAction);
        Assert.Equal(BookingCancellationReasonCategory.Emergency.ToString(), response.CancellationReasonCategory);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, response.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, response.RefundDecisionReason);

        // Paid, cancelled after acceptance and never refunded: the fact-aware
        // state is the existing failed-refund outcome, not a new one.
        Assert.Equal(BookingRefundState.Failed, response.RefundState);

        // The pre-existing fields keep their meaning: the booking's own reason
        // and timestamps are unchanged, and the persisted lifecycle instants are
        // reported rather than replaced by the fact's instants.
        Assert.Equal(booking.Status, response.Status);
        Assert.Equal(booking.CancellationReason, response.CancellationReason);
        Assert.Equal(booking.CancelledOnUtc, response.CancelledOnUtc);
        Assert.Equal(booking.PaidOnUtc, response.PaidOnUtc);
        Assert.Equal(booking.ConfirmedOnUtc, response.ConfirmedOnUtc);
    }

    [Fact]
    public async Task Detail_reports_no_cancellation_facts_when_none_was_recorded()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CancellationActorSide);
        Assert.Null(result.Value.CancellationAction);
        Assert.Null(result.Value.CancellationReasonCategory);
        Assert.Null(result.Value.RefundEntitlement);
        Assert.Null(result.Value.RefundDecisionReason);
        Assert.Equal(BookingRefundState.NotApplicable, result.Value.RefundState);
    }

    [Fact]
    public async Task Detail_keeps_the_caregiver_only_boundary_and_never_serves_another_caregivers_booking()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var owner = CaregiverId.New();
        var stranger = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, owner, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var other = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(stranger, booking.Id), CancellationToken.None);
        var unknown = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(owner, BookingId.New()), CancellationToken.None);

        foreach (var denied in new[] { other, unknown })
        {
            Assert.False(denied.IsSuccess);
            Assert.Equal("Bookings.NotFound", denied.Error.Code);
        }
    }

    [Fact]
    public async Task Detail_never_exposes_the_caregiver_incident_flag_a_ledger_or_a_derived_timeline()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        UserId actorUserId = UserId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelledOnUtc = now.AddMinutes(2);
        booking.CancelByCaregiver("SECRET_BOOKING_REASON", cancelledOnUtc);
        BookingCancellationFact fact = CreateCaregiverCancelFact(
            booking.Id,
            actorUserId,
            booking.ConfirmedOnUtc!.Value,
            cancelledOnUtc,
            "SECRET_REASON_NOTE");

        dbContext.Bookings.Add(booking);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        string json = JsonSerializer.Serialize(result.Value);

        Assert.DoesNotContain("SECRET_REASON_NOTE", json, StringComparison.Ordinal);
        Assert.DoesNotContain(actorUserId.Value.ToString(), json, StringComparison.OrdinalIgnoreCase);

        foreach (string forbidden in new[]
                 {
                     "isCaregiverIncident", "reasonNote", "actorUserId", "policyVersion",
                     "statusAtCancellation", "ledger", "timeline", "eventHistory"
                 })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }

        // The only history-shaped member is the approved `visitHistory`, which
        // reads real stored reports; this booking has no report, so it stays
        // null and no history is fabricated.
        using JsonDocument document = JsonDocument.Parse(json);
        string[] historyMembers = document.RootElement
            .EnumerateObject()
            .Where(property => property.Name.Contains("History", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(new[] { "VisitHistory" }, historyMembers);
        Assert.Null(result.Value.VisitHistory);
    }

    [Fact]
    public async Task Detail_publishes_the_real_visit_history_and_the_family_review()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        UserId caregiverUserId = UserId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);

        VisitReport visitReport = VisitReport.Create(
            booking.Id,
            family.Id,
            elderly.Id,
            caregiverId,
            caregiverUserId,
            BookingCaregiverType.Medical,
            "Patient stable",
            "Walked around the room",
            "Water taken",
            VisitReportAssessment.NoImmediateConcern,
            now.AddHours(1),
            now.AddHours(2),
            now.AddHours(3));

        MedicalReport medicalReport = MedicalReport.Create(
            booking.Id,
            family.Id,
            elderly.Id,
            caregiverId,
            caregiverUserId,
            BookingCaregiverType.Medical,
            120,
            80,
            70,
            36.5m,
            "Morning reading",
            now.AddHours(2),
            VisitReportAssessment.NeedsFollowUp,
            now.AddHours(4),
            true,
            now.AddHours(3),
            caregiverUserId,
            "private/medical/photo.jpg");

        BookingReview review = BookingReview.Create(
            booking.Id,
            family.OwnerUserId,
            4,
            "Kind and punctual",
            true,
            now.AddHours(4));

        dbContext.VisitReports.Add(visitReport);
        dbContext.MedicalReports.Add(medicalReport);
        dbContext.BookingReviews.Add(review);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        BookingDetailResponse response = result.Value;

        // The history is the real stored reports of this booking, newest first.
        Assert.NotNull(response.VisitHistory);
        Assert.Equal(2, response.VisitHistory!.Count);
        CaregiverBookingVisitHistoryItem medical = response.VisitHistory[0];
        Assert.Equal("medical", medical.ReportType);
        Assert.Equal(medicalReport.Id.Value, medical.ReportId);
        Assert.Equal(120, medical.Systolic);
        Assert.Equal(80, medical.Diastolic);
        Assert.Equal(70, medical.Pulse);
        Assert.Equal(36.5m, medical.Temperature);
        Assert.Equal("Morning reading", medical.Notes);
        Assert.Equal(VisitReportAssessment.NeedsFollowUp, medical.Assessment);
        Assert.Equal(now.AddHours(2), medical.MeasurementTakenOnUtc);
        Assert.True(medical.PhotoAvailable);
        Assert.Null(medical.ObservedCondition);
        Assert.Null(medical.StartedOnUtc);

        CaregiverBookingVisitHistoryItem visit = response.VisitHistory[1];
        Assert.Equal("visit", visit.ReportType);
        Assert.Equal(visitReport.Id.Value, visit.ReportId);
        Assert.Equal("Patient stable", visit.ObservedCondition);
        Assert.Equal("Walked around the room", visit.Activities);
        Assert.Equal(now.AddHours(1), visit.StartedOnUtc);
        Assert.Equal(now.AddHours(2), visit.CompletedOnUtc);
        Assert.Null(visit.Systolic);
        Assert.False(visit.PhotoAvailable);

        // The family's review is the stored review, with its own anonymity flag.
        Assert.NotNull(response.FamilyReview);
        Assert.Equal(review.Id.Value, response.FamilyReview!.Id);
        Assert.Equal(booking.Id.Value, response.FamilyReview.BookingId);
        Assert.Equal(4, response.FamilyReview.Rating);
        Assert.Equal("Kind and punctual", response.FamilyReview.Comment);
        Assert.True(response.FamilyReview.IsAnonymous);
    }

    [Fact]
    public async Task Detail_keeps_upcoming_visits_null_and_never_maps_another_booking_as_a_visit()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);

        // A second, later booking of the same caregiver: it must never be
        // mapped as an "upcoming visit" of the first one.
        Booking later = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(9),
            new TimeOnly(14, 0),
            new TimeOnly(16, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(700m, 15m),
            now.AddHours(48),
            DateOnly.FromDateTime(now),
            now);

        dbContext.Bookings.AddRange(booking, later);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.UpcomingVisits);

        string json = JsonSerializer.Serialize(result.Value);

        Assert.DoesNotContain(later.Id.Value.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DateOnly.FromDateTime(now).AddDays(9).ToString("yyyy-MM-dd"), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_leaves_the_record_members_null_when_no_report_or_review_exists()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.VisitHistory);
        Assert.Null(result.Value.UpcomingVisits);
        Assert.Null(result.Value.FamilyReview);
    }

    [Fact]
    public async Task Detail_reports_the_stored_price_snapshot_amounts()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        BookingPriceSnapshot snapshot = booking.PriceSnapshot;
        Assert.Equal(snapshot.BaseCaregiverFee, result.Value.BaseCaregiverFee);
        Assert.Equal(snapshot.PlatformFeePercentage, result.Value.PlatformFeePercentage);
        Assert.Equal(snapshot.PlatformFeeAmount, result.Value.PlatformFeeAmount);
        Assert.Equal(snapshot.TotalPayableAmount, result.Value.TotalPayableAmount);
        Assert.Equal(snapshot.Currency, result.Value.Currency);
    }

    [Fact]
    public async Task Detail_never_exposes_the_emergency_contact_to_a_caregiver()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        elderly.SetEmergencyContact("Nour", "Daughter", "+201001234567");
        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Elderly.EmergencyContact);

        string json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("Nour", json, StringComparison.Ordinal);
        Assert.DoesNotContain("+201001234567", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completion_notes_persist_and_read_back_through_both_booking_detail_projections()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        await new CaregiverStartBookingCommandHandler(dbContext).Handle(
            new CaregiverStartBookingCommand(caregiverId, booking.Id, now.AddMinutes(2)),
            CancellationToken.None);

        const string notes = "الحالة العامة جيدة، تم إعطاء الدواء في موعده.";
        Result completed = await new CaregiverCompleteBookingCommandHandler(dbContext).Handle(
            new CaregiverCompleteBookingCommand(caregiverId, booking.Id, notes, now.AddMinutes(3)),
            CancellationToken.None);

        Assert.True(completed.IsSuccess);

        var caregiverRead = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);
        var familyRead = await new GetFamilyBookingDetailQueryHandler(dbContext)
            .Handle(new GetFamilyBookingDetailQuery(family.OwnerUserId, booking.Id), CancellationToken.None);

        Assert.True(caregiverRead.IsSuccess);
        Assert.True(familyRead.IsSuccess);

        // The read models — not just the entity — carry the submitted notes.
        Assert.Equal(BookingStatus.Completed, caregiverRead.Value.Status);
        Assert.Equal(notes, caregiverRead.Value.CaregiverNotes);
        Assert.Equal(notes, familyRead.Value.CaregiverNotes);
    }

    [Fact]
    public async Task Completion_without_notes_is_allowed_and_reads_back_null_through_both_projections()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        await new CaregiverStartBookingCommandHandler(dbContext).Handle(
            new CaregiverStartBookingCommand(caregiverId, booking.Id, now.AddMinutes(2)),
            CancellationToken.None);

        Result completed = await new CaregiverCompleteBookingCommandHandler(dbContext).Handle(
            new CaregiverCompleteBookingCommand(caregiverId, booking.Id, null, now.AddMinutes(3)),
            CancellationToken.None);

        Assert.True(completed.IsSuccess);

        var caregiverRead = await new GetCaregiverBookingDetailQueryHandler(dbContext)
            .Handle(new GetCaregiverBookingDetailQuery(caregiverId, booking.Id), CancellationToken.None);
        var familyRead = await new GetFamilyBookingDetailQueryHandler(dbContext)
            .Handle(new GetFamilyBookingDetailQuery(family.OwnerUserId, booking.Id), CancellationToken.None);

        Assert.True(caregiverRead.IsSuccess);
        Assert.True(familyRead.IsSuccess);
        Assert.Equal(BookingStatus.Completed, caregiverRead.Value.Status);
        Assert.Null(caregiverRead.Value.CaregiverNotes);
        Assert.Null(familyRead.Value.CaregiverNotes);
    }

    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }
    private static (FamiliesDbContext DbContext, Family Family, Elderly Elderly) SeedFamily()
    {
        var dbContext = CreateDbContext();
        var family = Family.Create(UserId.New(), "Caregiver Detail Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        dbContext.Families.Add(family);
        dbContext.Elderlies.Add(elderly);
        dbContext.SaveChanges();

        return (dbContext, family, elderly);
    }

    private static Booking ConfirmedBooking(
        Family family,
        Elderly elderly,
        CaregiverId caregiverId,
        DateTime now)
    {
        var booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddHours(24),
            DateOnly.FromDateTime(now),
            now);

        booking.MarkAsPaid(booking.Id.Value.ToString(), $"txn-{Guid.NewGuid():N}", now);
        booking.AcceptByCaregiver(now.AddMinutes(1));

        return booking;
    }

    private static BookingCancellationFact CreateCaregiverCancelFact(
        BookingId bookingId,
        UserId actorUserId,
        DateTime confirmedOnUtc,
        DateTime cancelledOnUtc,
        string note)
    {
        var feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            note);

        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed,
                confirmedOnUtc,
                startedOnUtc: null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));

        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }
}
