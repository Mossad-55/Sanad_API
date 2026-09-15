using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class AccountExclusivityModelTests
{
    [Fact]
    public void Model_EnforcesOneCaregiverAndPreservesPerTypeUniqueness()
    {
        // Model-only: no database connection. Runtime PostgreSQL race proof is an owner gate.
        using var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=unused").Options);
        var entity = Assert.Single(db.GetService<IDesignTimeModel>().Model.GetEntityTypes(), e => e.GetTableName() == "user_accounts");
        var index = Assert.Single(entity.GetIndexes(), i => i.GetDatabaseName() == "ux_user_accounts_one_caregiver");
        Assert.True(index.IsUnique);
        Assert.Equal("account_type IN (2, 3)", index.GetFilter());
        Assert.Equal("UserId", Assert.Single(index.Properties).Name);
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Count == 2 &&
            i.GetDatabaseName() == "IX_user_accounts_user_id_account_type");
    }
}
