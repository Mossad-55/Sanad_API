using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.CareRecords;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Regression for the caregiver care-record recipient picker
/// (<c>ListCaregiverRecipientsQueryHandler</c>, route
/// <c>GET /api/v1/family/care-records/recipients</c>) which 500s on
/// PostgreSQL whenever a <c>search</c> is sent and on every listing order:
/// the previous <c>e.ArabicFullName.Value.ToLower().Contains(...)</c> search
/// and <c>.OrderBy(e =&gt; e.ArabicFullName.Value)</c> shapes unwrap members
/// of the value-converted name properties and are rejected by the Npgsql
/// translator. The Npgsql facts compile the exact production composition
/// (extracted as <c>CaregiverRecipientPickerQuery</c>) with
/// <c>ToQueryString</c> WITHOUT opening any connection, so they prove
/// PostgreSQL translation rather than InMemory behavior (InMemory never runs
/// the relational translator). The same defect family is already pinned for
/// the resident read by <c>CareHomeResidentsPostgresTests</c>.
/// </summary>
public sealed class CaregiverRecipientPickerTranslationTests
{
    private static FamiliesDbContext CreateNpgsqlDb() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_probe_never_opened;Username=probe;Password=probe")
            .Options);

    private static readonly DateTime GraceCutoffUtc = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void To_like_pattern_escapes_metacharacters_literally()
    {
        // "\\" -> "\\\\", "%" -> "\\%", "_" -> "\\_"; everything wrapped in
        // the same %...% substring semantics the needle previously had. The
        // normalized search is already trimmed/lowercased by
        // CareRecordPageQuery.Normalize, so no further trimming happens here.
        Assert.Equal("%needle%", CaregiverRecipientPickerQuery.ToLikePattern("needle"));
        Assert.Equal("%50\\%off%", CaregiverRecipientPickerQuery.ToLikePattern("50%off"));
        Assert.Equal("%a\\_b%", CaregiverRecipientPickerQuery.ToLikePattern("a_b"));
        Assert.Equal("%a\\\\b%", CaregiverRecipientPickerQuery.ToLikePattern("a\\b"));
    }

    [Fact]
    public void Production_picker_page_composition_translates_on_postgresql()
    {
        using FamiliesDbContext db = CreateNpgsqlDb();

        string sql = CaregiverRecipientPickerQuery
            .Page(
                db,
                new CaregiverId(Guid.NewGuid()),
                GraceCutoffUtc,
                "needle",
                page: 2,
                pageSize: 20)
            .ToQueryString();

        // Translated end to end: the ILIKE name search with an explicit
        // escape character, the full mapped ordering and the SQL-side paging
        // all survive as SQL.
        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arabic_full_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);

        // No `.Value` member access on the converted name/id properties may
        // survive into the SQL (the pinned failure witness is
        // Value_member_name_search_and_order_are_rejected_by_npgsql).
        Assert.DoesNotContain(".Value", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_picker_scope_queries_translate_on_postgresql()
    {
        using FamiliesDbContext db = CreateNpgsqlDb();

        // Without a search: only the booking-window EXISTS. This is the shape
        // the CountAsync and the no-search listing execute.
        string scopeSql = CaregiverRecipientPickerQuery
            .AuthorizedRecipients(db, new CaregiverId(Guid.NewGuid()), GraceCutoffUtc, null)
            .ToQueryString();
        Assert.Contains("EXISTS", scopeSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bookings", scopeSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ILIKE", scopeSql, StringComparison.OrdinalIgnoreCase);

        // With a search: the same scope plus ILIKE + ESCAPE on both mapped
        // name columns — both alternatives are translated, none is
        // client-evaluated.
        string searchedSql = CaregiverRecipientPickerQuery
            .AuthorizedRecipients(db, new CaregiverId(Guid.NewGuid()), GraceCutoffUtc, "needle")
            .ToQueryString();
        Assert.Contains("EXISTS", searchedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bookings", searchedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ILIKE", searchedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", searchedSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ToLower", searchedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Value_member_name_search_and_order_are_rejected_by_npgsql()
    {
        // Permanent regression witnesses for exactly what PostgreSQL refuses:
        // the original picker search predicate and the original ordering.
        // If either ever starts translating (or a reversion lands), the
        // production composition must be re-derived deliberately.
        using FamiliesDbContext db = CreateNpgsqlDb();
        string lowered = "x";

        IQueryable<ElderlyId> byValueNameSearch = db.Elderlies
            .Where(e =>
                e.ArabicFullName.Value.ToLower().Contains(lowered) ||
                e.EnglishFullName.Value.ToLower().Contains(lowered))
            .Select(e => e.Id);

        IQueryable<ElderlyId> byValueNameOrder = db.Elderlies
            .OrderBy(e => e.ArabicFullName.Value)
            .ThenBy(e => e.Id)
            .Select(e => e.Id);

        AssertCannotTranslate(byValueNameSearch);
        AssertCannotTranslate(byValueNameOrder);
    }

    private static void AssertCannotTranslate<T>(IQueryable<T> query)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => query.ToQueryString());
        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
