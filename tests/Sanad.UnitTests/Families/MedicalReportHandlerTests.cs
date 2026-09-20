using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Reports;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Reports;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class MedicalReportHandlerTests
{
    [Fact]
    public async Task CompletedMedicalBooking_SucceedsWithNotesAndNoConsentDetails()
    {
        using var db = CreateDb(); var booking = CreateBooking(BookingCaregiverType.Medical, true);
        db.Bookings.Add(booking); await db.SaveChangesAsync();
        var result = await Handler(db).Handle(Command(booking, "  Stable  "), CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.Equal("Stable", result.Value.Notes); Assert.False(result.Value.PhotoAvailable);
        Assert.DoesNotContain("Consent", string.Join(',', result.Value.GetType().GetProperties().Select(x => x.Name)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompanionBooking_ReturnsForbidden()
    {
        using var db = CreateDb(); var booking = CreateBooking(BookingCaregiverType.Companion, true);
        db.Bookings.Add(booking); await db.SaveChangesAsync();
        var result = await Handler(db).Handle(Command(booking, null), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal("Reports.Medical.CaregiverForbidden", result.Error.Code);
    }

    [Fact]
    public async Task ForeignBooking_ReturnsNotFound()
    {
        using var db = CreateDb(); var booking = CreateBooking(BookingCaregiverType.Medical, true);
        db.Bookings.Add(booking); await db.SaveChangesAsync();
        var result = await Handler(db).Handle(Command(booking, null) with { CaregiverId = CaregiverId.New() }, CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal("Bookings.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task IncompleteBooking_ReturnsInvalidOperation()
    {
        using var db = CreateDb(); var booking = CreateBooking(BookingCaregiverType.Medical, false);
        db.Bookings.Add(booking); await db.SaveChangesAsync();
        var result = await Handler(db).Handle(Command(booking, null), CancellationToken.None);
        Assert.False(result.IsSuccess); Assert.Equal("Bookings.Domain.InvalidOperation", result.Error.Code);
    }

    [Fact]
    public async Task DuplicateBooking_ReturnsAlreadySubmitted()
    {
        using var db = CreateDb(); var booking = CreateBooking(BookingCaregiverType.Medical, true);
        db.Bookings.Add(booking); await db.SaveChangesAsync(); var handler = Handler(db);
        Assert.True((await handler.Handle(Command(booking, null), CancellationToken.None)).IsSuccess);
        var duplicate = await handler.Handle(Command(booking, null), CancellationToken.None);
        Assert.False(duplicate.IsSuccess); Assert.Equal("Reports.Medical.AlreadySubmitted", duplicate.Error.Code);
    }

    private static SubmitMedicalReportCommand Command(Booking b, string? notes) => new(b.CaregiverId, UserId.New(), b.Id, null, null, null, null, notes, null, VisitReportAssessment.NotAssessed, false, DateTime.UtcNow, null, null, 0);
    private static SubmitMedicalReportCommandHandler Handler(FamiliesDbContext db) => new(db, new NoOpStorage());
    private static Booking CreateBooking(BookingCaregiverType type, bool completed)
    {
        DateTime now = DateTime.UtcNow;
        var b = Booking.Create(FamilyId.New(), UserId.New(), ElderlyId.New(), CaregiverId.New(), type, BookingShiftType.HomeVisit, DateOnly.FromDateTime(now.AddDays(1)), new TimeOnly(10), new TimeOnly(12), "Address", null, BookingPriceSnapshot.Calculate(500, 15), now.AddDays(1), DateOnly.FromDateTime(now), now);
        b.MarkAsPaid("order", "transaction", now); b.AcceptByCaregiver(now); b.StartVisit(now); if (completed) b.CompleteVisit(null, now); return b;
    }
    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoOpStorage : IFileStorage
    {
        public Task<Result<StoredFile>> SaveAsync(Stream c, string t, long l, string f, CancellationToken x = default) => Task.FromResult<Result<StoredFile>>(new Error("Storage.File.Unsupported", "not used"));
        public Task<Result<StoredFile>> SavePrivateAsync(Stream c, string t, long l, string f, CancellationToken x = default) => Task.FromResult<Result<StoredFile>>(new Error("Storage.File.Unsupported", "not used"));
        public Task<Result<PrivateFileContent>> OpenReadAsync(string k, CancellationToken x = default) => Task.FromResult<Result<PrivateFileContent>>(new Error("Storage.File.NotFound", "not used"));
        public Task<Result> DeleteAsync(string k, CancellationToken x = default) => Task.FromResult(Result.Success());
    }
}
