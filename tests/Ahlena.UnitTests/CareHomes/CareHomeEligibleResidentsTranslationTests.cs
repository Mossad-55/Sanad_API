using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Visits;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// Regression for the family eligible-residents read that 500s on the
/// rehearsal with "The LINQ expression ... .ThenBy(c =&gt; c.ElderlyId.Value)
/// could not be translated" (journald 2026-10-26 09:38:32, handler
/// <c>GetEligibleCareHomeVisitResidentsQueryHandler</>). The Npgsql facts
/// compile the exact production composition (extracted as
/// <c>GetEligibleCareHomeVisitResidentsQueryHandler.EligibleResidentRows</c>)
/// with <c>ToQueryString</c> WITHOUT opening any connection, so they prove
/// PostgreSQL translation rather than InMemory behavior (InMemory never runs
/// the relational translator).
/// </summary>
public sealed class CareHomeEligibleResidentsTranslationTests
{
    private static CareHomesDbContext CreateNpgsqlDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_probe;Username=probe;Password=probe").Options);

    [Fact]
    public void Eligible_resident_rows_translate_on_postgresql()
    {
        using var db = CreateNpgsqlDb();

        string sql = GetEligibleCareHomeVisitResidentsQueryHandler.EligibleResidentRows(
                db,
                new CareHomeId(Guid.NewGuid()),
                new FamilyId(Guid.NewGuid()),
                new DateOnly(2026, 10, 26))
            .ToQueryString();

        // Translated end to end: the eligibility filter and the full
        // ordering survive as SQL.
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ElderlyId", sql, StringComparison.OrdinalIgnoreCase);

        // The typed-id ordering key is the mapped column, not an unwrapped
        // member access: no `.Value` projection may survive into the SQL.
        Assert.DoesNotContain(".Value", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Unwrapped_typed_id_ordering_is_not_translatable_on_postgresql()
    {
        using var db = CreateNpgsqlDb();

        IQueryable<Ahlena.Modules.CareHomes.Domain.Bookings.CareHomeBooking> oldShape =
            db.Bookings
                .OrderBy(x => x.ElderlyArabicName)
                .ThenBy(x => x.ElderlyId.Value);

        // Pins the failure mode: the previous `.ElderlyId.Value` ordering
        // fails translation (the reported 500), so this regression cannot
        // silently pass against an InMemory provider instead.
        Assert.Throws<InvalidOperationException>(() => oldShape.ToQueryString());
    }
}
