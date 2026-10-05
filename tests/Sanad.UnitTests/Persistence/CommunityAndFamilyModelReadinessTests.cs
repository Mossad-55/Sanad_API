using Microsoft.EntityFrameworkCore;
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
