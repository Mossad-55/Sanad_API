using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Model metadata of the reviewed <see cref="BookingCancellationFact"/> mapping: table and schema,
/// every snake_case column with its exact nullability, strong-id converters, integer enums, the
/// unique booking index, the restricted foreign key, the deliberate absence of any reverse
/// navigation on <see cref="Booking"/>, and — since B1-C — the interface's read-only exposure
/// of the fact set (the write path remains the recorder alone).
/// <para>
/// Metadata only. The design-time model is inspected with the InMemory provider, like the existing
/// model tests, so nothing here connects to PostgreSQL and no converter round-trip is claimed: the
/// InMemory provider skips value conversions when it stores and materializes values.
/// </para>
/// </summary>
public sealed class BookingCancellationFactMappingTests
{
    private const string FactTableName = "booking_cancellation_facts";

    private const string BookingUniqueIndexName = "ux_booking_cancellation_facts_booking";

    // ---------------------------------------------------------------------------------------------
    // Table, schema and identity of the mapped entity.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Model_ShouldMapFactTableInTheFamiliesSchema()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        IEntityType entityType = GetFactEntityType(dbContext);

        Assert.Equal(typeof(BookingCancellationFact), entityType.ClrType);
        Assert.Equal(FactTableName, entityType.GetTableName());

        // "families" is the context default schema, not a per-entity override.
        Assert.Equal("families", entityType.GetSchema());
        Assert.Equal(FamiliesDbContext.Schema, entityType.GetSchema());
        Assert.Equal(FamiliesDbContext.Schema, dbContext.Model.GetDefaultSchema());

        // A table of its own: the fact is not folded into the booking aggregate as an owned type.
        Assert.False(entityType.IsOwned());
    }

    [Fact]
    public void Model_ShouldMapFactKeyAsAppAssignedWithGuidConverter()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        IEntityType entityType = GetFactEntityType(dbContext);

        IKey? key = entityType.FindPrimaryKey();
        Assert.NotNull(key);

        IProperty keyProperty = Assert.Single(key!.Properties);

        Assert.Equal(nameof(BookingCancellationFact.Id), keyProperty.Name);
        Assert.Equal("id", keyProperty.GetColumnName());

        // The application owns the id; the database never generates one.
        Assert.Equal(ValueGenerated.Never, keyProperty.ValueGenerated);

        // BookingCancellationFactId <-> Guid, asserted from metadata only.
        AssertHasValueConverter(
            keyProperty,
            typeof(BookingCancellationFactId),
            typeof(Guid));
    }

    [Fact]
    public void Model_ShouldMapStronglyTypedForeignKeysWithGuidConverters()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        IEntityType entityType = GetFactEntityType(dbContext);

        AssertHasValueConverter(
            GetProperty(entityType, nameof(BookingCancellationFact.BookingId)),
            typeof(BookingId),
            typeof(Guid));

        AssertHasValueConverter(
            GetProperty(entityType, nameof(BookingCancellationFact.ActorUserId)),
            typeof(UserId),
            typeof(Guid));
    }

    // ---------------------------------------------------------------------------------------------
    // Every mapped member: exact snake_case column name.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(BookingCancellationFact.Id), "id")]
    [InlineData(nameof(BookingCancellationFact.BookingId), "booking_id")]
    [InlineData(nameof(BookingCancellationFact.ActorSide), "actor_side")]
    [InlineData(nameof(BookingCancellationFact.ActorUserId), "actor_user_id")]
    [InlineData(nameof(BookingCancellationFact.Action), "action")]
    [InlineData(nameof(BookingCancellationFact.StatusAtCancellation), "status_at_cancellation")]
    [InlineData(nameof(BookingCancellationFact.CancelledOnUtc), "cancelled_on_utc")]
    [InlineData(nameof(BookingCancellationFact.ConfirmedOnUtcUsed), "confirmed_on_utc_used")]
    [InlineData(nameof(BookingCancellationFact.PolicyVersion), "policy_version")]
    [InlineData(nameof(BookingCancellationFact.ReasonCategory), "reason_category")]
    [InlineData(nameof(BookingCancellationFact.ReasonNote), "reason_note")]
    [InlineData(nameof(BookingCancellationFact.RefundEntitlement), "refund_entitlement")]
    [InlineData(nameof(BookingCancellationFact.RefundDecisionReason), "refund_decision_reason")]
    [InlineData(nameof(BookingCancellationFact.IsCaregiverIncident), "is_caregiver_incident")]
    public void Model_ShouldMapEveryFactMemberToItsReviewedColumn(
        string memberName,
        string columnName)
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        Assert.Equal(
            columnName,
            GetProperty(GetFactEntityType(dbContext), memberName).GetColumnName());
    }

    // ---------------------------------------------------------------------------------------------
    // Required versus nullable, exactly as reviewed.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(BookingCancellationFact.Id), false)]
    [InlineData(nameof(BookingCancellationFact.BookingId), false)]
    [InlineData(nameof(BookingCancellationFact.ActorSide), false)]
    [InlineData(nameof(BookingCancellationFact.ActorUserId), false)]
    [InlineData(nameof(BookingCancellationFact.Action), false)]
    [InlineData(nameof(BookingCancellationFact.StatusAtCancellation), false)]
    [InlineData(nameof(BookingCancellationFact.CancelledOnUtc), false)]
    [InlineData(nameof(BookingCancellationFact.ConfirmedOnUtcUsed), true)]
    [InlineData(nameof(BookingCancellationFact.PolicyVersion), false)]
    [InlineData(nameof(BookingCancellationFact.ReasonCategory), true)]
    [InlineData(nameof(BookingCancellationFact.ReasonNote), true)]
    [InlineData(nameof(BookingCancellationFact.RefundEntitlement), false)]
    [InlineData(nameof(BookingCancellationFact.RefundDecisionReason), false)]
    [InlineData(nameof(BookingCancellationFact.IsCaregiverIncident), false)]
    public void Model_ShouldMapFactColumnNullabilityExactly(
        string memberName,
        bool isNullable)
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        Assert.Equal(
            isNullable,
            GetProperty(GetFactEntityType(dbContext), memberName).IsNullable);
    }

    // ---------------------------------------------------------------------------------------------
    // Enums are stored as their numeric value, never as text and never remapped.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(BookingCancellationFact.ActorSide))]
    [InlineData(nameof(BookingCancellationFact.Action))]
    [InlineData(nameof(BookingCancellationFact.StatusAtCancellation))]
    [InlineData(nameof(BookingCancellationFact.ReasonCategory))]
    [InlineData(nameof(BookingCancellationFact.RefundEntitlement))]
    [InlineData(nameof(BookingCancellationFact.RefundDecisionReason))]
    public void Model_ShouldStoreFactEnumsAsIntegerColumns(string memberName)
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        IProperty property = GetProperty(GetFactEntityType(dbContext), memberName);

        // HasConversion<int>() pins the provider (database) type to int for every enum column,
        // including the nullable reason category.
        Assert.Equal(typeof(int), property.GetProviderClrType());

        // The CLR side is still the enum itself (nullable for the reason category).
        Assert.True(
            (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).IsEnum);
    }

    [Fact]
    public void Model_ShouldLimitReasonNoteToTheBookingReasonLength()
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        IProperty reasonNote = GetProperty(
            GetFactEntityType(dbContext),
            nameof(BookingCancellationFact.ReasonNote));

        Assert.Equal("reason_note", reasonNote.GetColumnName());
        Assert.True(reasonNote.IsNullable);
        Assert.Equal(Booking.MaximumReasonLength, reasonNote.GetMaxLength());

        // The mapped bound is the reviewed 500 characters, not a new length.
        Assert.Equal(500, Booking.MaximumReasonLength);
    }

    // ---------------------------------------------------------------------------------------------
    // One durable fact per booking, and a restricted relationship to Booking.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Model_ShouldMapUniqueBookingIndexWithTheReviewedName()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        IEntityType entityType = GetFactEntityType(dbContext);

        IIndex index = Assert.Single(
            entityType.GetIndexes(),
            candidate => candidate.GetDatabaseName() == BookingUniqueIndexName);

        Assert.True(index.IsUnique);
        Assert.Equal(
            nameof(BookingCancellationFact.BookingId),
            Assert.Single(index.Properties).Name);
        Assert.Equal("booking_id", Assert.Single(index.Properties).GetColumnName());
    }

    [Fact]
    public void Model_ShouldMapRestrictedForeignKeyToBookingWithoutNavigation()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        IEntityType entityType = GetFactEntityType(dbContext);

        IForeignKey foreignKey = Assert.Single(entityType.GetForeignKeys());

        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal(typeof(Booking), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(
            nameof(BookingCancellationFact.BookingId),
            Assert.Single(foreignKey.Properties).Name);

        // It targets Booking's own strongly typed key.
        IProperty principalKeyProperty =
            Assert.Single(foreignKey.PrincipalKey.Properties);

        Assert.Equal(nameof(Booking.Id), principalKeyProperty.Name);
        Assert.Equal(typeof(BookingId), principalKeyProperty.ClrType);

        // Booking is only the principal: neither side carries a navigation.
        Assert.Null(foreignKey.DependentToPrincipal);
        Assert.Null(foreignKey.PrincipalToDependent);
    }

    // ---------------------------------------------------------------------------------------------
    // The aggregate stays untouched; the interface serves the fact set read-only (B1-C).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Model_ShouldKeepBookingFreeOfAnyNavigationToFact()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        IEntityType bookingEntityType = GetBookingEntityType(dbContext);

        // EF model: no navigation and no skip navigation points from Booking at a fact.
        Assert.DoesNotContain(
            bookingEntityType.GetNavigations(),
            navigation =>
                navigation.TargetEntityType.ClrType == typeof(BookingCancellationFact));
        Assert.DoesNotContain(
            bookingEntityType.GetSkipNavigations(),
            navigation =>
                navigation.TargetEntityType.ClrType == typeof(BookingCancellationFact));

        // CLR surface: Booking declares no member that could carry facts.
        Assert.DoesNotContain(
            typeof(Booking).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => CouldCarryFacts(property.PropertyType));
    }

    [Fact]
    public void Model_ShouldExposeFactsReadOnlyOnTheFamiliesApplicationInterface()
    {
        using FamiliesDbContext dbContext = CreateDbContext();

        // Positive control: the concrete context does serve the fact table.
        Assert.NotNull(dbContext.BookingCancellationFacts);
        Assert.IsAssignableFrom<IFamiliesDbContext>(dbContext);

        // B1-C ruling: the admin read models (cancellation summary, cancellations history,
        // fact-aware refund state) read facts through the module interface, so the interface
        // deliberately exposes the set for read access. The protection this guard originally
        // carried — that the application layer never writes a fact — stands unchanged: the sole
        // write seam remains IBookingCancellationFactRecorder, and the entity is append-only
        // (pinned by BookingCancellationFactAppendOnlyGuardTests).
        Assert.Contains(
            typeof(IFamiliesDbContext).GetMembers(),
            member => member.Name == nameof(FamiliesDbContext.BookingCancellationFacts));
        Assert.Contains(
            typeof(IFamiliesDbContext).GetProperties(),
            property => property.PropertyType == typeof(DbSet<BookingCancellationFact>));

        // Control for the reflection itself: the interface still exposes the mapped Bookings set.
        Assert.Contains(
            typeof(IFamiliesDbContext).GetProperties(),
            property => property.PropertyType == typeof(DbSet<Booking>));
    }

    [Fact]
    public void Model_ShouldMapEveryFactClrMemberWithoutShadowProperties()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        IEntityType entityType = GetFactEntityType(dbContext);

        Assert.DoesNotContain(
            entityType.GetProperties(),
            property => property.IsShadowProperty());

        // Every declared CLR member is mapped; nothing is silently ignored.
        foreach (PropertyInfo property in typeof(BookingCancellationFact)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.NotNull(entityType.FindProperty(property.Name));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------------------------------------

    private static bool CouldCarryFacts(Type propertyType) =>
        propertyType == typeof(BookingCancellationFact)
        || typeof(IEnumerable<BookingCancellationFact>).IsAssignableFrom(propertyType);

    private static void AssertHasValueConverter(
        IProperty property,
        Type modelClrType,
        Type providerClrType)
    {
        Assert.Equal(modelClrType, property.ClrType);

        // Probe the configured converter itself: lambda-built strong-id conversions do not write
        // the relational provider-CLR annotation (EF returns null there for them), unlike the
        // generic HasConversion<int>() form asserted on the enum columns.
        Assert.NotNull(property.GetValueConverter());
        Assert.Equal(modelClrType, property.GetValueConverter()!.ModelClrType);
        Assert.Equal(providerClrType, property.GetValueConverter()!.ProviderClrType);
    }

    private static IProperty GetProperty(
        IEntityType entityType,
        string propertyName)
    {
        IProperty? property = entityType.FindProperty(propertyName);

        Assert.NotNull(property);

        return property!;
    }

    private static IEntityType GetFactEntityType(FamiliesDbContext dbContext) =>
        FindEntityType(dbContext, typeof(BookingCancellationFact));

    private static IEntityType GetBookingEntityType(FamiliesDbContext dbContext) =>
        FindEntityType(dbContext, typeof(Booking));

    private static IEntityType FindEntityType(
        FamiliesDbContext dbContext,
        Type clrType)
    {
        // The design-time model is where the relational facets (table, column, index names) live;
        // building it never connects to a database.
        IEntityType? entityType = dbContext
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(clrType);

        Assert.NotNull(entityType);

        return entityType!;
    }

    private static FamiliesDbContext CreateDbContext()
    {
        DbContextOptions<FamiliesDbContext> options =
            new DbContextOptionsBuilder<FamiliesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new FamiliesDbContext(options);
    }
}