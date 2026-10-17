using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Persistence;

public sealed class CommunityAndFamilyModelReadinessTests
{
    [Fact]
    public void CommunityModel_GeneratesPostgresSchemaWithoutConnectingOrWritingMigrationFiles()
    {
        var options = new DbContextOptionsBuilder<CommunityDbContext>()
            .UseNpgsql("Host=localhost;Database=sanad_model_only;Username=unused;Password=unused")
            .Options;
        using var context = new CommunityDbContext(options);

        var sql = context.Database.GenerateCreateScript();

        Assert.Contains("community", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Interactions", sql, StringComparison.Ordinal);
        Assert.Contains("timestamp with time zone", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CURRENT_TIMESTAMP", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommunityModel_GeneratesModerationTablesConstraintsAndIndexes()
    {
        var options = new DbContextOptionsBuilder<CommunityDbContext>()
            .UseNpgsql("Host=localhost;Database=sanad_model_only;Username=unused;Password=unused")
            .Options;
        using var context = new CommunityDbContext(options);

        var sql = context.Database.GenerateCreateScript();

        // FUR-07 entities and the retained-rejection columns.
        Assert.Contains("CommunityPostReports", sql, StringComparison.Ordinal);
        Assert.Contains("CommunityAuthorRestrictions", sql, StringComparison.Ordinal);
        Assert.Contains("RejectionReasonCode", sql, StringComparison.Ordinal);
        Assert.Contains("RejectionNote", sql, StringComparison.Ordinal);
        Assert.Contains("RejectedOnUtc", sql, StringComparison.Ordinal);

        // One retained report per reporter/post pair is a unique index.
        Assert.Contains(
            "IX_CommunityPostReports_PostId_ReporterId", sql, StringComparison.Ordinal);
        var uniqueIndex = sql.IndexOf(
            "CREATE UNIQUE INDEX \"IX_CommunityPostReports_PostId_ReporterId\"",
            StringComparison.Ordinal);
        Assert.True(uniqueIndex >= 0, "The deduplication index must be unique.");

        // Report-to-post cascade and the active-restriction lookups.
        Assert.Contains(
            "FK_CommunityPostReports_Posts_PostId", sql, StringComparison.Ordinal);
        Assert.Contains(
            "IX_CommunityAuthorRestrictions_ExpiresOnUtc", sql, StringComparison.Ordinal);
        Assert.Contains(
            "IX_CommunityAuthorRestrictions_RevokedOnUtc", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CommunityModel_MatchesLastMigrationSnapshotAndIncludesModerationMigration()
    {
        var options = new DbContextOptionsBuilder<CommunityDbContext>()
            .UseNpgsql("Host=localhost;Database=sanad_model_only;Username=unused;Password=unused")
            .Options;
        using var context = new CommunityDbContext(options);

        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();
        Assert.Contains(
            migrations.Migrations.Keys,
            id => id.EndsWith("_AddCommunityModeration", StringComparison.Ordinal));

        // The generated snapshot must already describe every table the current
        // model maps (including the two FUR-07 entities), so the authored
        // migration needs no further model delta.
        string[] snapshotTables = TablesOf(migrations.ModelSnapshot!.Model);
        string[] currentTables = TablesOf(context.GetService<IDesignTimeModel>().Model);

        Assert.Contains("CommunityPostReports", currentTables);
        Assert.Contains("CommunityAuthorRestrictions", currentTables);
        Assert.Equal(currentTables, snapshotTables);
    }

    private static string[] TablesOf(IModel model) => model.GetEntityTypes()
        .Select(entity => entity.GetTableName())
        .Where(name => name is not null)
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void FamiliesModel_GeneratesTypedReviewAndGrantSchemaWithoutConnecting()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseNpgsql("Host=localhost;Database=sanad_model_only;Username=unused;Password=unused")
            .Options;
        using var context = new FamiliesDbContext(options);

        var sql = context.Database.GenerateCreateScript();

        Assert.Contains("BookingReviews", sql, StringComparison.Ordinal);
        Assert.Contains("MedicalAccessGrants", sql, StringComparison.Ordinal);
        Assert.Contains("RevokedOnUtc", sql, StringComparison.Ordinal);
    }
}
