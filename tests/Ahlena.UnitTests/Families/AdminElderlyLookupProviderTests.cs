using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Caregivers.Application.Lookups;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// Provider-level facts for the dependent (elderly) name+id lookup: the exact
/// query shape is translated through the real <see cref="FamiliesDbContext"/>
/// Npgsql mapping with <c>ToQueryString</c>, which never opens a connection.
/// This is the proof that matters for the two value-converted name columns —
/// an in-process SQLite model does not exercise the PostgreSQL compound of the
/// text operations, and a member of the converted type (for example
/// <c>.Value</c>) is refused by PostgreSQL outright. The pattern rules of both
/// lookup routes are asserted here too, since the caregiver SQL only ever runs
/// against PostgreSQL.
/// </summary>
public sealed class AdminElderlyLookupProviderTests
{
    [Fact]
    public void Search_translates_with_ilike_escape_and_the_mapped_name_columns()
    {
        using FamiliesDbContext db = CreateFamiliesDb(ProbeConnectionString);

        string sql = AdminElderlyLookupQuery
            .LiveDependents(db, "Mansour")
            .ToQueryString();

        Assert.Contains("families.elderlies", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arabic_full_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("english_full_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);

        // Dependents of a deleted family stay excluded in the translated SQL.
        Assert.Contains("families.families", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deleted_on_utc", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Page_query_translates_with_a_total_order_bounded_offset_and_a_narrow_projection()
    {
        using FamiliesDbContext db = CreateFamiliesDb(ProbeConnectionString);

        string sql = AdminElderlyLookupQuery
            .Page(db, "Mansour", page: 3, pageSize: 50)
            .ToQueryString();

        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);

        // The projection is the two names and the id only: the entity, its
        // owned medical profile and every contact, address and note column are
        // never selected, so a page cannot drag clinical data out of the
        // database even though the payload would drop it.
        Assert.Contains("arabic_full_name", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("elderly_medical_profiles", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("health_notes", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("detailed_address", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("emergency_contact", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("date_of_birth", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blank_search_drops_the_ilike_predicate_entirely()
    {
        using FamiliesDbContext db = CreateFamiliesDb(ProbeConnectionString);

        string sql = AdminElderlyLookupQuery
            .LiveDependents(db, "   ")
            .ToQueryString();

        Assert.DoesNotContain("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("families.elderlies", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_very_large_page_saturates_the_skip_instead_of_overflowing()
    {
        Assert.Equal(0, AdminElderlyLookupQuery.ComputeSkip(page: 1, pageSize: 20));
        Assert.Equal(1900, AdminElderlyLookupQuery.ComputeSkip(page: 20, pageSize: 100));

        // (page - 1) * pageSize in 32-bit arithmetic would wrap negative here
        // and produce "OFFSET -…", which PostgreSQL rejects as a 500.
        Assert.Equal(
            int.MaxValue,
            AdminElderlyLookupQuery.ComputeSkip(page: int.MaxValue, pageSize: 100));
        Assert.True(
            AdminElderlyLookupQuery.ComputeSkip(page: 50_000_000, pageSize: 100) > 0);
    }

    [Fact]
    public void Caller_wildcards_are_escaped_in_both_lookup_routes()
    {
        // "%", "_" and the escape character itself become literal text; the
        // only wildcards are the two the server adds around the needle.
        Assert.Equal("%mansour%", AdminElderlyLookupQuery.ToLikePattern("  mansour  "));
        Assert.Equal(
            "%100\\%\\_done%",
            AdminElderlyLookupQuery.ToLikePattern("100%_done"));
        Assert.Equal("%a\\\\b%", AdminElderlyLookupQuery.ToLikePattern("a\\b"));

        Assert.Equal("%mansour%", AdminCaregiverLookupQuery.ToLikePattern(" mansour "));
        Assert.Equal(
            "%100\\%\\_done%",
            AdminCaregiverLookupQuery.ToLikePattern("100%_done"));
        Assert.Equal("%a\\\\b%", AdminCaregiverLookupQuery.ToLikePattern("a\\b"));

        // No search means no predicate rather than a match-everything pattern.
        Assert.Null(AdminCaregiverLookupQuery.ToLikePattern(null));
        Assert.Null(AdminCaregiverLookupQuery.ToLikePattern("   "));

        Assert.Equal("\\", AdminElderlyLookupQuery.LikeEscapeCharacter);
        Assert.Equal("\\", AdminCaregiverLookupQuery.LikeEscapeCharacter);
    }

    private static string ProbeConnectionString =>
        "Host=localhost;Database=translation_probe_never_opened;Username=probe;Password=probe";

    private static FamiliesDbContext CreateFamiliesDb(string connectionString) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseNpgsql(connectionString)
            .Options);
}
