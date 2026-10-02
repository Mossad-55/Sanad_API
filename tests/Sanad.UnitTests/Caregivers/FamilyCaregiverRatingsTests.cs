using Microsoft.EntityFrameworkCore;
using FluentValidation.TestHelper;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

public sealed class FamilyCaregiverRatingsTests
{
    [Fact]
    public void Validator_RejectsOutOfRangeStarsAndOverlongReviewText()
    {
        var validator = new RateCompletedCaregiverBookingCommandValidator();
        var invalidStars = new RateCompletedCaregiverBookingCommand(
            Guid.NewGuid(), UserId.New(), 0, null, DateTime.UtcNow);
        var overlongText = new RateCompletedCaregiverBookingCommand(
            Guid.NewGuid(), UserId.New(), 5,
            new string('x', CaregiverRating.MaximumReviewTextLength + 1),
            DateTime.UtcNow);

        validator.TestValidate(invalidStars)
            .ShouldHaveValidationErrorFor(command => command.Stars);
        validator.TestValidate(overlongText)
            .ShouldHaveValidationErrorFor(command => command.ReviewText);
    }

    [Fact]
    public void Create_RequiresEligibleIdentifiersAndOneToFiveStars()
    {
        DateTime now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        Assert.Throws<DomainException>(() => CaregiverRating.Create(
            CaregiverId.Empty,
            Guid.NewGuid(),
            FamilyId.New(),
            UserId.New(),
            5,
            null,
            now));
        Assert.Throws<DomainException>(() => CaregiverRating.Create(
            CaregiverId.New(),
            Guid.NewGuid(),
            FamilyId.New(),
            UserId.New(),
            0,
            null,
            now));
        Assert.Throws<DomainException>(() => CaregiverRating.Create(
            CaregiverId.New(),
            Guid.NewGuid(),
            FamilyId.New(),
            UserId.New(),
            6,
            null,
            now));
    }

    [Fact]
    public void Edit_NormalizesTextAndPreservesOriginalCreationTime()
    {
        DateTime created = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-1), DateTimeKind.Utc);
        DateTime edited = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        UserId firstActor = UserId.New();
        UserId secondActor = UserId.New();
        CaregiverRating rating = CaregiverRating.Create(
            CaregiverId.New(),
            Guid.NewGuid(),
            FamilyId.New(),
            firstActor,
            3,
            "  Helpful  ",
            created);

        rating.Edit(secondActor, 5, "  Excellent care  ", edited);

        Assert.Equal(5, rating.Stars);
        Assert.Equal("Excellent care", rating.ReviewText);
        Assert.Equal(created, rating.CreatedOnUtc);
        Assert.Equal(edited, rating.UpdatedOnUtc);
        Assert.Equal(firstActor, rating.CreatedByUserId);
        Assert.Equal(secondActor, rating.UpdatedByUserId);
    }

    [Fact]
    public async Task RateCompletedBooking_CreatesThenEditsOneRatingAndRefreshesSummary()
    {
        using CaregiversDbContext db = CreateDbContext();
        UserId caregiverUserId = UserId.New();
        Caregiver caregiver = Caregiver.Create(caregiverUserId, CaregiverType.Companion);
        db.Caregivers.Add(caregiver);
        await db.SaveChangesAsync();

        Guid bookingId = Guid.NewGuid();
        Guid familyId = Guid.NewGuid();
        UserId actor = UserId.New();
        var handler = new RateCompletedCaregiverBookingCommandHandler(
            db,
            new EligibleBooking(familyId, caregiver.Id));
        DateTime now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        var created = await handler.Handle(
            new RateCompletedCaregiverBookingCommand(
                bookingId,
                actor,
                3,
                "Good service",
                now),
            default);

        var edited = await handler.Handle(
            new RateCompletedCaregiverBookingCommand(
                bookingId,
                actor,
                5,
                "Very good",
                now.AddMinutes(1)),
            default);

        Assert.True(created.IsSuccess);
        Assert.True(edited.IsSuccess);
        Assert.Equal(created.Value.RatingId, edited.Value.RatingId);
        Assert.Equal(1, await db.CaregiverRatings.CountAsync());
        Assert.Equal(5, edited.Value.Stars);
        Assert.Equal(5m, caregiver.AverageRating);
        Assert.Equal(1, caregiver.ReviewsCount);
    }

    private static CaregiversDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class EligibleBooking(Guid familyId, CaregiverId caregiverId)
        : ICaregiverBookingRatingEligibility
    {
        public Task<AuthorizedCompletedCaregiverBooking?> GetEligibleBookingAsync(
            Guid bookingId,
            UserId actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AuthorizedCompletedCaregiverBooking?>(
                new AuthorizedCompletedCaregiverBooking(familyId, caregiverId));
    }
}
