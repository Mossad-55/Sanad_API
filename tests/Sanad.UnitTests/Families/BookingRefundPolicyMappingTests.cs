using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Model metadata of the revision-2 refund mapping on <see cref="Booking"/>: the three new booking
/// columns (the preserved policy revision, the approved document date it came from, and the money
/// already refunded) plus the owned <c>booking_session_allocations</c> table (Owner decision #5).
/// <para>
/// Metadata only, inspected through the design-time model with the InMemory provider, like the
/// existing model tests. Nothing here connects to PostgreSQL.
/// </para>
/// <para>
/// NOT RUN. Authored in the W3-F1 slice; the single build/test gate belongs to Mastermind.
/// </para>
/// </summary>
public sealed class BookingRefundPolicyMappingTests
{
    // ---------------------------------------------------------------------------------------------
    // The preserved policy revision.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Model_ShouldMapRefundPolicyVersionWithLegacyDefault()
    {
        IProperty property = GetBookingProperty(nameof(Booking.RefundPolicyVersion));

        Assert.Equal("refund_policy_version", property.GetColumnName());
        Assert.False(property.IsNullable);
        Assert.Equal(BookingRefundPolicyVersion.LegacyVersion, property.GetDefaultValue());

        // Existing rows default to the legacy revision: no backfill can silently reprice them.
        Assert.Equal(1, BookingRefundPolicyVersion.LegacyVersion);
    }

    [Fact]
    public void Model_ShouldMapRefundPolicyDocumentDate()
    {
        IProperty property = GetBookingProperty(nameof(Booking.RefundPolicyDocumentEffectiveOn));

        Assert.Equal("refund_policy_document_effective_on", property.GetColumnName());
        Assert.False(property.IsNullable);
        Assert.Equal("date", property.GetColumnType());
    }

    [Fact]
    public void Model_ShouldMapRefundedMinorUnitsWithZeroDefault()
    {
        IProperty property = GetBookingProperty(nameof(Booking.RefundedMinorUnits));

        Assert.Equal("refunded_minor_units", property.GetColumnName());
        Assert.False(property.IsNullable);
        Assert.Equal(typeof(long), property.ClrType);
        Assert.Equal(0L, property.GetDefaultValue());
    }

    // ---------------------------------------------------------------------------------------------
    // Decision #17 ambiguity marker columns.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Model_ShouldMapTheAmbiguityMarkerWithAFalseDefault()
    {
        IProperty property = GetBookingProperty(nameof(Booking.IsRefundOutcomeAmbiguous));

        Assert.Equal("is_refund_outcome_ambiguous", property.GetColumnName());
        Assert.False(property.IsNullable);
        Assert.Equal(false, property.GetDefaultValue());
    }

    [Fact]
    public void Model_ShouldMapEveryAmbiguityEvidenceColumnToItsSnakeCaseColumn()
    {
        Assert.Equal(
            "refund_ambiguity_reference",
            GetBookingProperty(nameof(Booking.RefundAmbiguityReference)).GetColumnName());
        Assert.Equal(
            "refund_ambiguity_reason",
            GetBookingProperty(nameof(Booking.RefundAmbiguityReason)).GetColumnName());
        Assert.Equal(
            "refund_ambiguity_raised_on_utc",
            GetBookingProperty(nameof(Booking.RefundAmbiguityRaisedOnUtc)).GetColumnName());
        Assert.Equal(
            "refund_ambiguity_award_minor_units",
            GetBookingProperty(nameof(Booking.RefundAmbiguityAwardMinorUnits)).GetColumnName());
        Assert.Equal(
            "refund_ambiguity_cleared_on_utc",
            GetBookingProperty(nameof(Booking.RefundAmbiguityClearedOnUtc)).GetColumnName());
        Assert.Equal(
            "refund_ambiguity_cleared_by",
            GetBookingProperty(nameof(Booking.RefundAmbiguityClearedBy)).GetColumnName());
    }

    // ---------------------------------------------------------------------------------------------
    // Revision-2 money evidence on the cancellation fact (MAJ-6).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Model_ShouldMapTheFactAwardAndPartialEntitlementAsNullableColumns()
    {
        IEntityType factType = FindEntityType(typeof(BookingCancellationFact));

        IProperty awarded = factType.FindProperty(nameof(BookingCancellationFact.AwardedMinorUnits))!;
        IProperty partial = factType
            .FindProperty(nameof(BookingCancellationFact.PartialEntitlementMinorUnits))!;

        Assert.Equal("awarded_minor_units", awarded.GetColumnName());
        Assert.True(awarded.IsNullable);
        Assert.Equal("partial_entitlement_minor_units", partial.GetColumnName());
        Assert.True(partial.IsNullable);
    }

    // ---------------------------------------------------------------------------------------------
    // Database-level shape guards on the allocation table (minor 14).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Model_ShouldGuardTheAllocationTableWithCheckConstraints()
    {
        IEntityType allocationType = FindAllocationEntityType()!;

        IEnumerable<string> constraints = allocationType.GetCheckConstraints()
            .Select(constraint => constraint.Name!);

        Assert.Contains("ck_booking_session_allocations_outcome_range", constraints);
        Assert.Contains("ck_booking_session_allocations_allocated_non_negative", constraints);
    }

    // ---------------------------------------------------------------------------------------------
    // The owned session allocation table (decision #5).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Model_ShouldMapSessionAllocationsAsAnOwnedTableWithCompositeKey()
    {
        IEntityType? allocationType = FindAllocationEntityType();

        Assert.NotNull(allocationType);
        Assert.Equal("booking_session_allocations", allocationType!.GetTableName());
        Assert.Equal(FamiliesDbContext.Schema, allocationType.GetSchema());

        IKey key = Assert.Single(
            allocationType.GetKeys(),
            candidate => candidate.IsPrimaryKey());

        Assert.Equal(2, key.Properties.Count);
        Assert.Contains(key.Properties, p => p.Name == "BookingId");
        Assert.Contains(key.Properties, p => p.Name == "SessionIndex");
    }

    [Fact]
    public void Model_ShouldMapEveryAllocationMemberToItsSnakeCaseColumn()
    {
        IEntityType allocationType = FindAllocationEntityType()!;

        Assert.Equal(
            "booking_id",
            allocationType.FindProperty("BookingId")!.GetColumnName());
        Assert.Equal(
            "session_index",
            allocationType.FindProperty(nameof(BookingSessionAllocation.SessionIndex))!.GetColumnName());
        Assert.Equal(
            "allocated_minor_units",
            allocationType.FindProperty(nameof(BookingSessionAllocation.AllocatedMinorUnits))!.GetColumnName());
        Assert.Equal(
            "outcome",
            allocationType.FindProperty(nameof(BookingSessionAllocation.Outcome))!.GetColumnName());
        Assert.Equal(
            "delivered_on_utc",
            allocationType.FindProperty(nameof(BookingSessionAllocation.DeliveredOnUtc))!.GetColumnName());
    }

    [Fact]
    public void Model_ShouldStoreTheAllocationOutcomeAsAnInteger()
    {
        IProperty outcome = FindAllocationEntityType()!
            .FindProperty(nameof(BookingSessionAllocation.Outcome))!;

        Assert.Equal(typeof(int), outcome.GetProviderClrType());
        Assert.True(outcome.ClrType.IsEnum);
    }

    [Fact]
    public void Model_ShouldMapAllocationForeignKeyToBookingWithCascadeDelete()
    {
        IEntityType allocationType = FindAllocationEntityType()!;

        IForeignKey foreignKey = Assert.Single(allocationType.GetForeignKeys());

        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(typeof(Booking), foreignKey.PrincipalEntityType.ClrType);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------------------------------------

    private static IProperty GetBookingProperty(string name)
    {
        IProperty? property = FindBookingEntityType().FindProperty(name);

        Assert.NotNull(property);

        return property!;
    }

    private static IEntityType FindBookingEntityType() =>
        FindEntityType(typeof(Booking));

    private static IEntityType? FindAllocationEntityType() =>
        FindDesignTimeModel().FindEntityType("Sanad.Modules.Families.Domain.Bookings.BookingSessionAllocation");

    private static IEntityType FindEntityType(Type clrType)
    {
        IEntityType? entityType = FindDesignTimeModel().FindEntityType(clrType);

        Assert.NotNull(entityType);

        return entityType!;
    }

    private static IModel FindDesignTimeModel()
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        return new FamiliesDbContext(options);
    }
}
