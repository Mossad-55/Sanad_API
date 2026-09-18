using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Append-only behaviour of the reviewed <see cref="FamiliesDbContext"/> save guard: a
/// <see cref="BookingCancellationFact"/> can be appended and read back, while a tracked fact in
/// <see cref="EntityState.Modified"/> or <see cref="EntityState.Deleted"/> fails every save entry
/// point before anything reaches the store.
/// <para>
/// The guard inspects the change tracker before the base save runs, so it behaves identically above
/// every provider: the InMemory provider is a faithful host for these tests. Stored rows are always
/// compared through a fresh context, never through the mutated context's own change tracker.
/// </para>
/// </summary>
public sealed class BookingCancellationFactAppendOnlyGuardTests
{
    private const string ReasonNote = "family emergency at home";

    private const int MutatedPolicyVersion = 99;

    private static readonly DateTime AcceptanceOnUtc =
        new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CancelWithinGraceOnUtc =
        AcceptanceOnUtc.AddMinutes(10);

    // ---------------------------------------------------------------------------------------------
    // Append path: a valid fact is stored and all of its recorded values survive the round-trip.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SaveChanges_AddedFact_PersistsTheRecordedDecision()
    {
        string databaseName = Guid.NewGuid().ToString();
        BookingId bookingId = BookingId.New();
        UserId actorUserId = UserId.New();

        using (FamiliesDbContext arrangeContext = CreateDbContext(databaseName))
        {
            BookingCancellationFact fact = CreateFact(bookingId, actorUserId);

            arrangeContext.BookingCancellationFacts.Add(fact);

            Assert.Equal(EntityState.Added, arrangeContext.Entry(fact).State);
            Assert.Equal(1, arrangeContext.SaveChanges());
            Assert.Equal(EntityState.Unchanged, arrangeContext.Entry(fact).State);
        }

        using FamiliesDbContext assertContext = CreateDbContext(databaseName);

        BookingCancellationFact reloaded =
            Assert.Single(assertContext.BookingCancellationFacts);

        Assert.Equal(EntityState.Unchanged, assertContext.Entry(reloaded).State);
        AssertRecordedDecision(reloaded, bookingId, actorUserId);
    }

    [Fact]
    public async Task SaveChangesAsync_AddedFact_PersistsTheRecordedDecision()
    {
        string databaseName = Guid.NewGuid().ToString();
        BookingId bookingId = BookingId.New();
        UserId actorUserId = UserId.New();

        using (FamiliesDbContext arrangeContext = CreateDbContext(databaseName))
        {
            BookingCancellationFact fact = CreateFact(bookingId, actorUserId);

            arrangeContext.BookingCancellationFacts.Add(fact);

            Assert.Equal(EntityState.Added, arrangeContext.Entry(fact).State);
            Assert.Equal(1, await arrangeContext.SaveChangesAsync(CancellationToken.None));
            Assert.Equal(EntityState.Unchanged, arrangeContext.Entry(fact).State);
        }

        using FamiliesDbContext assertContext = CreateDbContext(databaseName);

        BookingCancellationFact reloaded =
            Assert.Single(assertContext.BookingCancellationFacts);

        Assert.Equal(EntityState.Unchanged, assertContext.Entry(reloaded).State);
        AssertRecordedDecision(reloaded, bookingId, actorUserId);
    }

    // ---------------------------------------------------------------------------------------------
    // A tracked mutation is rejected on all four save entry points, before persistence.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SaveChanges_ModifiedFact_ThrowsBeforePersisting()
    {
        await AssertMutationIsRejectedAsync(
            SaveEntryPoint.Synchronous,
            EntityState.Modified);
    }

    [Fact]
    public async Task SaveChanges_AcceptAllChangesModifiedFact_ThrowsBeforePersisting()
    {
        await AssertMutationIsRejectedAsync(
            SaveEntryPoint.SynchronousAcceptAllChanges,
            EntityState.Modified);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedFact_ThrowsBeforePersisting()
    {
        await AssertMutationIsRejectedAsync(
            SaveEntryPoint.Asynchronous,
            EntityState.Modified);
    }

    [Fact]
    public async Task SaveChangesAsync_AcceptAllChangesModifiedFact_ThrowsBeforePersisting()
    {
        await AssertMutationIsRejectedAsync(
            SaveEntryPoint.AsynchronousAcceptAllChanges,
            EntityState.Modified);
    }

    [Fact]
    public async Task SaveChanges_DeletedFact_ThrowsBeforePersisting()
    {
        await AssertMutationIsRejectedAsync(
            SaveEntryPoint.Synchronous,
            EntityState.Deleted);
    }

    [Fact]
    public async Task SaveChanges_AcceptAllChangesDeletedFact_ThrowsBeforePersisting()
    {
        await AssertMutationIsRejectedAsync(
            SaveEntryPoint.SynchronousAcceptAllChanges,
            EntityState.Deleted);
    }

    [Fact]
    public async Task SaveChangesAsync_DeletedFact_ThrowsBeforePersisting()
    {
        await AssertMutationIsRejectedAsync(
            SaveEntryPoint.Asynchronous,
            EntityState.Deleted);
    }

    [Fact]
    public async Task SaveChangesAsync_AcceptAllChangesDeletedFact_ThrowsBeforePersisting()
    {
        await AssertMutationIsRejectedAsync(
            SaveEntryPoint.AsynchronousAcceptAllChanges,
            EntityState.Deleted);
    }

    // ---------------------------------------------------------------------------------------------
    // States that must not be blocked.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SaveChanges_DetachedFact_IsNeitherBlockedNorPersisted()
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        // A fact that was never added stays Detached: no guard, and nothing is written for it.
        BookingCancellationFact fact = CreateFact(BookingId.New(), UserId.New());

        Assert.Equal(EntityState.Detached, dbContext.Entry(fact).State);
        Assert.Equal(0, dbContext.SaveChanges());
        Assert.Empty(dbContext.BookingCancellationFacts);
    }

    [Fact]
    public void SaveChanges_UnchangedFactTrackedWithAnotherEntity_StillSavesThatEntity()
    {
        string databaseName = Guid.NewGuid().ToString();
        SeededFact seeded = SeedFact(databaseName);

        using FamiliesDbContext dbContext = CreateDbContext(databaseName);

        BookingCancellationFact tracked = dbContext.BookingCancellationFacts
            .Single(fact => fact.Id == seeded.Fact.Id);

        Assert.Equal(EntityState.Unchanged, dbContext.Entry(tracked).State);

        Booking booking = AddUnrelatedBooking(dbContext);

        int written = dbContext.SaveChanges();

        // One row, or more when the aggregate owns rows: the exact provider count is not the contract.
        Assert.True(written > 0);
        Assert.Equal(EntityState.Unchanged, dbContext.Entry(booking).State);
        Assert.Equal(EntityState.Unchanged, dbContext.Entry(tracked).State);

        AssertUnrelatedBookingAndFactAreStored(databaseName, seeded, booking);
    }

    [Fact]
    public async Task SaveChangesAsync_UnchangedFactTrackedWithAnotherEntity_StillSavesThatEntity()
    {
        string databaseName = Guid.NewGuid().ToString();
        SeededFact seeded = SeedFact(databaseName);

        using FamiliesDbContext dbContext = CreateDbContext(databaseName);

        BookingCancellationFact tracked = dbContext.BookingCancellationFacts
            .Single(fact => fact.Id == seeded.Fact.Id);

        Assert.Equal(EntityState.Unchanged, dbContext.Entry(tracked).State);

        Booking booking = AddUnrelatedBooking(dbContext);

        int written = await dbContext.SaveChangesAsync(CancellationToken.None);

        // One row, or more when the aggregate owns rows: the exact provider count is not the contract.
        Assert.True(written > 0);
        Assert.Equal(EntityState.Unchanged, dbContext.Entry(booking).State);
        Assert.Equal(EntityState.Unchanged, dbContext.Entry(tracked).State);

        AssertUnrelatedBookingAndFactAreStored(databaseName, seeded, booking);
    }

    // ---------------------------------------------------------------------------------------------
    // Discarding the rejected mutation restores the context without touching the stored row.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SaveChanges_AfterDiscardingTheBlockedMutation_Succeeds()
    {
        string databaseName = Guid.NewGuid().ToString();
        SeededFact seeded = SeedFact(databaseName);

        using FamiliesDbContext dbContext = CreateDbContext(databaseName);

        BookingCancellationFact tracked = dbContext.BookingCancellationFacts
            .Single(fact => fact.Id == seeded.Fact.Id);
        EntityEntry<BookingCancellationFact> entry = dbContext.Entry(tracked);

        entry.Property(nameof(BookingCancellationFact.PolicyVersion)).CurrentValue =
            MutatedPolicyVersion;
        entry.State = EntityState.Modified;

        Assert.Throws<InvalidOperationException>(() =>
        {
            dbContext.SaveChanges();
        });

        // Discarding the mutation — what the guard message asks for — unblocks the context.
        entry.State = EntityState.Unchanged;

        Assert.Equal(EntityState.Unchanged, entry.State);
        Assert.Equal(0, dbContext.SaveChanges());

        // Appending is still allowed after a discarded mutation.
        BookingCancellationFact appended = CreateFact(BookingId.New(), UserId.New());

        dbContext.BookingCancellationFacts.Add(appended);

        Assert.Equal(1, dbContext.SaveChanges());

        using FamiliesDbContext assertContext = CreateDbContext(databaseName);

        Assert.Single(
            assertContext.BookingCancellationFacts,
            fact => fact.Id == appended.Id);

        // The original recorded decision is still the stored one, unchanged.
        AssertRecordedDecision(
            assertContext.BookingCancellationFacts.Single(fact => fact.Id == seeded.Fact.Id),
            seeded.BookingId,
            seeded.ActorUserId);
    }

    // ---------------------------------------------------------------------------------------------
    // Shared behaviour helpers.
    // ---------------------------------------------------------------------------------------------

    private static async Task AssertMutationIsRejectedAsync(
        SaveEntryPoint entryPoint,
        EntityState blockedState)
    {
        string databaseName = Guid.NewGuid().ToString();
        SeededFact seeded = SeedFact(databaseName);

        using FamiliesDbContext dbContext = CreateDbContext(databaseName);

        BookingCancellationFact tracked = dbContext.BookingCancellationFacts
            .Single(fact => fact.Id == seeded.Fact.Id);
        EntityEntry<BookingCancellationFact> entry = dbContext.Entry(tracked);

        Assert.Equal(EntityState.Unchanged, entry.State);

        if (blockedState == EntityState.Modified)
        {
            // A real, visible change that a provider would otherwise write.
            entry.Property(nameof(BookingCancellationFact.PolicyVersion)).CurrentValue =
                MutatedPolicyVersion;
            entry.Property(nameof(BookingCancellationFact.IsCaregiverIncident)).CurrentValue = true;
            entry.State = EntityState.Modified;
        }
        else
        {
            entry.State = EntityState.Deleted;
        }

        Assert.Equal(blockedState, entry.State);

        InvalidOperationException error = await ExpectGuardRejectionAsync(dbContext, entryPoint);

        // The guard message names the entity, the blocked state and the fact.
        Assert.Contains(nameof(BookingCancellationFact), error.Message);
        Assert.Contains(blockedState.ToString(), error.Message);
        Assert.Contains(seeded.Fact.Id.Value.ToString(), error.Message);

        // It fired before persistence: the stored row is still present and untouched.
        AssertStoreStillHoldsRecordedDecision(databaseName, seeded);

        // Nothing was written, and the rejected mutation is still tracked until it is discarded.
        Assert.Equal(blockedState, entry.State);
    }

    private static async Task<InvalidOperationException> ExpectGuardRejectionAsync(
        FamiliesDbContext dbContext,
        SaveEntryPoint entryPoint)
    {
        switch (entryPoint)
        {
            case SaveEntryPoint.Synchronous:
                return Assert.Throws<InvalidOperationException>(() =>
                {
                    dbContext.SaveChanges();
                });

            case SaveEntryPoint.SynchronousAcceptAllChanges:
                return Assert.Throws<InvalidOperationException>(() =>
                {
                    dbContext.SaveChanges(acceptAllChangesOnSuccess: true);
                });

            case SaveEntryPoint.Asynchronous:
                return await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await dbContext.SaveChangesAsync(CancellationToken.None));

            case SaveEntryPoint.AsynchronousAcceptAllChanges:
                return await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await dbContext.SaveChangesAsync(
                        acceptAllChangesOnSuccess: true,
                        CancellationToken.None));

            default:
                throw new ArgumentOutOfRangeException(nameof(entryPoint));
        }
    }

    private static void AssertStoreStillHoldsRecordedDecision(
        string databaseName,
        SeededFact seeded)
    {
        using FamiliesDbContext assertContext = CreateDbContext(databaseName);

        BookingCancellationFact stored = assertContext.BookingCancellationFacts
            .Single(fact => fact.Id == seeded.Fact.Id);

        AssertRecordedDecision(stored, seeded.BookingId, seeded.ActorUserId);
    }

    private static void AssertUnrelatedBookingAndFactAreStored(
        string databaseName,
        SeededFact seeded,
        Booking booking)
    {
        using FamiliesDbContext assertContext = CreateDbContext(databaseName);

        Assert.Single(assertContext.Bookings, stored => stored.Id == booking.Id);

        AssertRecordedDecision(
            assertContext.BookingCancellationFacts.Single(fact => fact.Id == seeded.Fact.Id),
            seeded.BookingId,
            seeded.ActorUserId);
    }

    private static Booking AddUnrelatedBooking(FamiliesDbContext dbContext)
    {
        // A booking is the principal of the fact's foreign key: saving one must not be disturbed by
        // an untouched fact sitting in the same change tracker.
        Booking booking = BookingCancellationBookingFactory.CreatePendingPaymentBooking();

        dbContext.Bookings.Add(booking);

        return booking;
    }

    /// <summary>
    /// Builds the persisted fact the guard tests use: a decision that only the policy can produce,
    /// recorded through the public <see cref="BookingCancellationFact.Create"/> factory.
    /// </summary>
    private static BookingCancellationFact CreateFact(
        BookingId bookingId,
        UserId actorUserId)
    {
        BookingCancellationFeedback feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            $"  {ReasonNote}  ");

        BookingCancellationDecision decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForFamilyCancellation(
                BookingStatus.Confirmed,
                AcceptanceOnUtc,
                startedOnUtc: null,
                CancelWithinGraceOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));

        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    private static SeededFact SeedFact(string databaseName)
    {
        BookingId bookingId = BookingId.New();
        UserId actorUserId = UserId.New();
        BookingCancellationFact fact = CreateFact(bookingId, actorUserId);

        using (FamiliesDbContext seedContext = CreateDbContext(databaseName))
        {
            seedContext.BookingCancellationFacts.Add(fact);
            seedContext.SaveChanges();
        }

        return new SeededFact(fact, bookingId, actorUserId);
    }

    private static void AssertRecordedDecision(
        BookingCancellationFact fact,
        BookingId expectedBookingId,
        UserId expectedActorUserId)
    {
        Assert.Equal(expectedBookingId, fact.BookingId);
        Assert.Equal(expectedActorUserId, fact.ActorUserId);
        Assert.Equal(BookingCancellationActorSide.Family, fact.ActorSide);
        Assert.Equal(BookingCancellationAction.Cancel, fact.Action);
        Assert.Equal(BookingStatus.Confirmed, fact.StatusAtCancellation);
        Assert.Equal(AcceptanceOnUtc, fact.ConfirmedOnUtcUsed);
        Assert.Equal(CancelWithinGraceOnUtc, fact.CancelledOnUtc);
        Assert.Equal(BookingCancellationPolicy.CurrentPolicyVersion, fact.PolicyVersion);
        Assert.Equal(BookingCancellationReasonCategory.Emergency, fact.ReasonCategory);
        Assert.Equal(ReasonNote, fact.ReasonNote);
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, fact.RefundEntitlement);
        Assert.Equal(
            BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow,
            fact.RefundDecisionReason);
        Assert.False(fact.IsCaregiverIncident);
    }

    private static FamiliesDbContext CreateDbContext(string? databaseName = null)
    {
        DbContextOptions<FamiliesDbContext> options =
            new DbContextOptionsBuilder<FamiliesDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
                .Options;

        return new FamiliesDbContext(options);
    }

    /// <summary>The four save entry points <see cref="FamiliesDbContext"/> overrides.</summary>
    private enum SaveEntryPoint
    {
        Synchronous,
        SynchronousAcceptAllChanges,
        Asynchronous,
        AsynchronousAcceptAllChanges
    }

    private sealed record SeededFact(
        BookingCancellationFact Fact,
        BookingId BookingId,
        UserId ActorUserId);
}
