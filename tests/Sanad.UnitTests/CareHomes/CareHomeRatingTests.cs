using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using FluentValidation.TestHelper;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Discovery;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeRatingTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Rating_requires_family_confirmed_check_in_and_edits_existing_booking_rating()
    {
        await using var db = CreateDb();
        CareHomeFacility facility = await AddApproved(db, "Rated home");
        FamilyId family = FamilyId.New();
        UserId actor = UserId.New();
        CareHomeBooking booking = AddBooking(db, facility.Id, family, actor, confirmed: true);
        CareHomeBooking unconfirmed = AddBooking(db, facility.Id, family, actor, confirmed: false);
        await db.SaveChangesAsync();
        var handler = new RateCareHomeBookingCommandHandler(db);

        var denied = await handler.Handle(new(unconfirmed.Id, actor, family, 5, null, Now), default);
        var created = await handler.Handle(new(booking.Id, actor, family, 3, "  Good  ", Now), default);
        var edited = await handler.Handle(new(booking.Id, actor, family, 5, " Excellent ", Now.AddMinutes(1)), default);

        Assert.False(denied.IsSuccess);
        Assert.Equal("CareHomes.Rating.BookingNotEligible", denied.Error.Code);
        Assert.True(created.IsSuccess);
        Assert.True(edited.IsSuccess);
        Assert.Equal(created.Value.RatingId, edited.Value.RatingId);
        Assert.Equal("Excellent", edited.Value.ReviewText);
        Assert.Equal(1, await db.Ratings.CountAsync(x => x.BookingId == booking.Id));
    }

    [Fact]
    public async Task Top_ten_excludes_unrated_and_ineligible_homes_and_uses_average_then_count_then_id()
    {
        await using var db = CreateDb();
        CareHomeFacility first = await AddApproved(db, "First");
        CareHomeFacility second = await AddApproved(db, "Second");
        CareHomeFacility tied = await AddApproved(db, "Tied");
        CareHomeFacility third = await AddApproved(db, "Third");
        CareHomeFacility unrated = await AddApproved(db, "Unrated");
        CareHomeFacility draft = CareHomeFacility.CreateDraft(UserId.New(), Now);
        db.Facilities.Add(draft);
        AddRating(db, first, 5);
        AddRating(db, second, 5);
        AddRating(db, second, 5);
        AddRating(db, tied, 5);
        AddRating(db, third, 4);
        AddRating(db, draft, 5);
        await db.SaveChangesAsync();

        var result = await new GetTopRatedCareHomesQueryHandler(db, new FixedClock(Now))
            .Handle(new(), default);

        Assert.True(result.IsSuccess);
        Guid[] expectedIds = new[] { first.Id, tied.Id }.OrderBy(x => x).Select(x => x.Value).ToArray();
        Assert.Equal(new[] { second.Id.Value }.Concat(expectedIds).Append(third.Id.Value), result.Value.Select(x => x.CareHomeId));
        Assert.DoesNotContain(result.Value, x => x.CareHomeId == unrated.Id.Value);
        Assert.Equal(2, result.Value[0].ReviewsCount);
        Assert.Equal(5m, result.Value[0].AverageRating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_rejects_stars_outside_one_to_five(int stars)
    {
        Assert.Throws<ArgumentException>(() => CareHomeRating.Create(Guid.NewGuid(), CareHomeId.New(),
            FamilyId.New(), UserId.New(), stars, null, Now));
    }

    [Fact]
    public void Controller_requires_family_access()
    {
        var authorization = Assert.Single(typeof(FamilyCareHomeRatingsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.FamilyAccess, authorization.Policy);
    }

    [Fact]
    public void Validator_and_error_mapping_match_rating_contract()
    {
        var validator = new RateCareHomeBookingCommandValidator();
        var invalid = new RateCareHomeBookingCommand(Guid.NewGuid(), UserId.New(), FamilyId.New(), 0,
            new string('x', CareHomeRating.MaximumReviewTextLength + 1), Now);
        Assert.NotEmpty(validator.TestValidate(invalid).Errors);
        Assert.Equal(404, ResultProblemDetailsMapper.Create(CareHomeRatingErrors.BookingNotEligible,
            new Microsoft.AspNetCore.Http.DefaultHttpContext()).Status);
        Assert.Equal(409, ResultProblemDetailsMapper.Create(CareHomeRatingErrors.RatingConflict,
            new Microsoft.AspNetCore.Http.DefaultHttpContext()).Status);
    }

    private static CareHomeBooking AddBooking(CareHomesDbContext db, CareHomeId facilityId,
        FamilyId familyId, UserId actor, bool confirmed)
    {
        var booking = CareHomeBooking.Create(facilityId, actor, familyId, ElderlyId.New(), Guid.NewGuid(),
            DateOnly.FromDateTime(Now.AddDays(3)), 1000, 0, 0, 1, "الاسم", "Name", 70, null,
            "Contact", null, null, null, Now);
        booking.MarkPaid(100, Now);
        booking.Accept(Now);
        booking.AssignPhysicalResource(Guid.NewGuid(), null, Now);
        booking.RecordCheckIn(actor, Now);
        if (confirmed) booking.ConfirmFamilyCheckIn(actor, Now);
        db.Bookings.Add(booking);
        return booking;
    }

    private static void AddRating(CareHomesDbContext db, CareHomeFacility facility, int stars) =>
        db.Ratings.Add(CareHomeRating.Create(Guid.NewGuid(), facility.Id, FamilyId.New(), UserId.New(),
            stars, null, Now));

    private static async Task<CareHomeFacility> AddApproved(CareHomesDbContext db, string name)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft() with { EnglishName = name }, Now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{name}-{type}.pdf", "application/pdf", 10, null, Now);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now);
        TimeZoneInfo cairo;
        try { cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch (TimeZoneNotFoundException) { cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, cairo)));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock(DateTime now) : IDateTimeProvider { public DateTime UtcNow { get; } = now; }
}
