using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// Model-lockstep proof for the care-home refund-policy lane (review item B3):
/// the EF model built from the actual CLR types and configurations — not a
/// second hand-written artifact — must map every refund-policy and
/// refund-ambiguity property onto exactly the snake_case column, store type,
/// and facets the migration trio adds. Building the model requires no database;
/// the Npgsql provider finalizes relational metadata offline. Asserting the
/// runtime model also pins the migration's compare basis: EF derives its
/// "pending changes" view from this very model, so any drift between the
/// configurations here and the migration/snapshot makes this suite fail
/// instead of surfacing as a migration mismatch later.
/// </summary>
public sealed class CareHomeRefundPolicyModelParityTests
{
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseNpgsql("Host=localhost;Database=ahlena_model_parity;Username=parity;Password=parity")
            .Options;
        using var context = new CareHomesDbContext(options);
        return context.Model;
    }

    private static IProperty Property(IModel model, Type entityType, string propertyName)
    {
        IEntityType entity = model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"The model has no entity type for {entityType.Name}.");
        return entity.FindProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"The model does not map {entityType.Name}.{propertyName} — exactly the lockstep gap this suite guards against.");
    }

    private static IProperty AssertColumn(
        IModel model,
        Type entityType,
        string propertyName,
        string table,
        string expectedColumn,
        string expectedColumnType,
        bool expectedNullable,
        int? expectedMaxLength = null,
        int? expectedPrecision = null,
        int? expectedScale = null)
    {
        IProperty property = Property(model, entityType, propertyName);
        string actualColumn = property.GetColumnName(StoreObjectIdentifier.Table(table, CareHomesDbContext.Schema))
            ?? throw new InvalidOperationException(
                $"{entityType.Name}.{propertyName} has no column on {table} — it is not mapped to storage.");

        var problems = new List<string>();
        if (actualColumn != expectedColumn) problems.Add($"column {actualColumn} != {expectedColumn}");
        if (property.GetColumnType() != expectedColumnType)
            problems.Add($"type {property.GetColumnType()} != {expectedColumnType}");
        if (property.IsNullable != expectedNullable) problems.Add($"nullable {property.IsNullable} != {expectedNullable}");
        if (property.GetMaxLength() != expectedMaxLength)
            problems.Add($"maxlength {property.GetMaxLength()?.ToString() ?? "none"} != {expectedMaxLength?.ToString() ?? "none"}");
        if (property.GetPrecision() != expectedPrecision)
            problems.Add($"precision {property.GetPrecision()?.ToString() ?? "none"} != {expectedPrecision?.ToString() ?? "none"}");
        if (property.GetScale() != expectedScale)
            problems.Add($"scale {property.GetScale()?.ToString() ?? "none"} != {expectedScale?.ToString() ?? "none"}");

        Assert.True(problems.Count == 0,
            $"{entityType.Name}.{propertyName} is out of lockstep: {string.Join("; ", problems)}.");
        return property;
    }

    [Fact]
    public void Booking_policy_and_ambiguity_columns_map_exactly_what_the_migration_adds()
    {
        IModel model = Model();
        const string table = "bookings";

        // The refund-policy snapshot columns.
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.RefundPolicyVersion),
            table, "refund_policy_version", "integer", expectedNullable: false);
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.RefundPolicyDocumentEffectiveOn),
            table, "refund_policy_document_effective_on", "date", expectedNullable: false);
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.CancellationTermsSnapshotJson),
            table, "cancellation_terms_snapshot_json", "text", expectedNullable: true);

        // The refund-ambiguity state-machine columns.
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.IsRefundOutcomeAmbiguous),
            table, "is_refund_outcome_ambiguous", "boolean", expectedNullable: false);
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.RefundAmbiguityReason),
            table, "refund_ambiguity_reason", "character varying(2000)", expectedNullable: true, expectedMaxLength: 2000);
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.RefundAmbiguityRaisedOnUtc),
            table, "refund_ambiguity_raised_on_utc", "timestamp with time zone", expectedNullable: true);
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.RefundAmbiguityAwardAmount),
            table, "refund_ambiguity_award_amount", "numeric(18,2)", expectedNullable: true,
            expectedPrecision: 18, expectedScale: 2);
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.RefundAmbiguityClearedOnUtc),
            table, "refund_ambiguity_cleared_on_utc", "timestamp with time zone", expectedNullable: true);
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.RefundAmbiguityClearedBy),
            table, "refund_ambiguity_cleared_by", "character varying(2000)", expectedNullable: true, expectedMaxLength: 2000);

        // The stable attempt identity added for the compare-and-swap fence.
        AssertColumn(model, typeof(CareHomeBooking), nameof(CareHomeBooking.RefundAttemptClaimId),
            table, "refund_attempt_claim_id", "uuid", expectedNullable: true);

        // The pre-existing optimistic concurrency token that fences every
        // reconcile and completion save is preserved.
        Assert.True(Property(model, typeof(CareHomeBooking), nameof(CareHomeBooking.Version)).IsConcurrencyToken,
            "The booking's version token must remain a concurrency token.");
    }

    [Fact]
    public void Every_refund_state_property_is_mapped_by_configuration_not_convention()
    {
        IModel model = Model();
        IEntityType bookings = model.FindEntityType(typeof(CareHomeBooking))
            ?? throw new InvalidOperationException("The model has no CareHomeBooking entity type.");

        // The exhaustive inventory of refund-state properties the aggregate
        // exposes, each with the exact column, store type, and facets the
        // model must carry. EVERY enumerated member is facet-asserted — a new
        // refund-state property added to the aggregate but left to convention
        // naming (or mapped with drifted facets) fails this suite on either
        // the set comparison or its facet row, so the B3 lockstep gap cannot
        // slip through by name alone. (RefundReference pins the pre-existing
        // convention-named text column that HEAD already shipped.)
        (string Property, string Column, string Type, bool Nullable, int? MaxLength, int? Precision, int? Scale)[] expectations =
        [
            ("IsRefundOutcomeAmbiguous", "is_refund_outcome_ambiguous", "boolean", false, null, null, null),
            ("RefundAmbiguityAwardAmount", "refund_ambiguity_award_amount", "numeric(18,2)", true, null, 18, 2),
            ("RefundAmbiguityClearedBy", "refund_ambiguity_cleared_by", "character varying(2000)", true, 2000, null, null),
            ("RefundAmbiguityClearedOnUtc", "refund_ambiguity_cleared_on_utc", "timestamp with time zone", true, null, null, null),
            ("RefundAmbiguityRaisedOnUtc", "refund_ambiguity_raised_on_utc", "timestamp with time zone", true, null, null, null),
            ("RefundAmbiguityReason", "refund_ambiguity_reason", "character varying(2000)", true, 2000, null, null),
            ("RefundAmount", "refund_amount", "numeric(18,2)", true, null, 18, 2),
            ("RefundAttemptClaimId", "refund_attempt_claim_id", "uuid", true, null, null, null),
            ("RefundClaimedOnUtc", "refund_claimed_on_utc", "timestamp with time zone", true, null, null, null),
            ("RefundCompletedBy", "refund_completed_by", "uuid", true, null, null, null),
            ("RefundCompletedOnUtc", "refund_completed_on_utc", "timestamp with time zone", true, null, null, null),
            ("RefundFailureReason", "refund_failure_reason", "character varying(2000)", true, 2000, null, null),
            ("RefundPolicyDocumentEffectiveOn", "refund_policy_document_effective_on", "date", false, null, null, null),
            ("RefundPolicyVersion", "refund_policy_version", "integer", false, null, null, null),
            ("RefundReference", "RefundReference", "text", true, null, null, null),
            ("RefundStatus", "refund_status", "integer", false, null, null, null),
            ("CancellationTermsSnapshotJson", "cancellation_terms_snapshot_json", "text", true, null, null, null)
        ];

        var refundFamily = bookings.GetProperties()
            .Where(p => p.Name.StartsWith("Refund", StringComparison.Ordinal)
                || p.Name is nameof(CareHomeBooking.IsRefundOutcomeAmbiguous)
                    or nameof(CareHomeBooking.CancellationTermsSnapshotJson))
            .Select(p => p.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            string.Join(",", expectations.Select(e => e.Property).OrderBy(n => n, StringComparer.Ordinal)),
            string.Join(",", refundFamily));

        foreach (var (property, column, type, nullable, maxLength, precision, scale) in expectations)
        {
            AssertColumn(model, typeof(CareHomeBooking), property, "bookings",
                column, type, nullable, maxLength, precision, scale);
        }
    }

    [Fact]
    public void Profile_revision_cancellation_terms_json_is_mapped_to_storage()
    {
        IModel model = Model();
        AssertColumn(model, typeof(CareHomeProfileRevision), nameof(CareHomeProfileRevision.CancellationTermsJson),
            "profile_revisions", "cancellation_terms_json", "text", expectedNullable: true);
    }

    [Fact]
    public void The_up_migration_backfills_attempt_identities_for_pre_deployment_claims()
    {
        // Static source proof (not a database execution): rows claimed by a
        // pre-deployment attempt carry refund_claimed_on_utc but no attempt
        // identity, so the reconcile compare-and-swap could never match them.
        // The AddCareHomeRefundPolicySnapshots Up must assign a stable
        // generated uuid to exactly those rows, after it adds the column and
        // as a data-only step (Designer/snapshot intentionally untouched).
        string migrationPath = WalkUpFor(Path.Combine(
            "src", "Modules", "CareHomes", "Infrastructure",
            "Ahlena.Modules.CareHomes.Infrastructure", "Persistence", "Migrations",
            "20261022213000_AddCareHomeRefundPolicySnapshots.cs"));
        string migration = File.ReadAllText(migrationPath);

        const string addColumnMarker = "name: \"refund_attempt_claim_id\"";
        const string backfill =
            "UPDATE care_homes.bookings SET refund_attempt_claim_id = gen_random_uuid() WHERE refund_claimed_on_utc IS NOT NULL AND refund_attempt_claim_id IS NULL;";

        Assert.Contains(addColumnMarker, migration);
        Assert.Contains(backfill, migration);
        Assert.True(
            migration.IndexOf(backfill, StringComparison.Ordinal)
                > migration.IndexOf(addColumnMarker, StringComparison.Ordinal),
            "The backfill must follow the AddColumn it populates.");
        // Scoped to live claims only: claimed rows never gain a second/made-up
        // identity when one exists to keep, and unclaimed rows stay NULL.
        Assert.Contains("WHERE refund_claimed_on_utc IS NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("AND refund_attempt_claim_id IS NULL", migration, StringComparison.Ordinal);
    }

    private static string WalkUpFor(string relativeUnderRepoRoot)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, relativeUnderRepoRoot)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"Could not locate {relativeUnderRepoRoot} above {AppContext.BaseDirectory}.")
            : Path.Combine(directory.FullName, relativeUnderRepoRoot);
    }
}
