using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class IdentityDbContextModelTests
{
    [Fact]
    public void Model_ShouldUseIdentitySchema()
    {
        using IdentityDbContext dbContext =
            CreateDbContext();

        Assert.Equal(
            IdentityDbContext.Schema,
            dbContext.Model.GetDefaultSchema());
    }

    [Theory]
    [InlineData("users")]
    [InlineData("user_accounts")]
    [InlineData("user_identity_documents")]
    [InlineData("verification_requests")]
    [InlineData("device_sessions")]
    public void Model_ShouldContainExpectedIdentityTables(
        string tableName)
    {
        using IdentityDbContext dbContext =
            CreateDbContext();

        bool tableExists =
            dbContext.Model
                .GetEntityTypes()
                .Any(entityType =>
                    entityType.GetTableName() ==
                    tableName);

        Assert.True(
            tableExists,
            $"Expected table '{tableName}' was not mapped.");
    }

    [Fact]
    public void Model_ShouldMapUniqueUserEmailAndPhoneIndexes()
    {
        using IdentityDbContext dbContext =
            CreateDbContext();

        var userEntityType =
            dbContext.Model
                .FindEntityType(
                    "Sanad.Modules.Identity.Domain.Users.User");

        Assert.NotNull(userEntityType);

        Assert.Contains(
            userEntityType!.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name == "Email");

        Assert.Contains(
            userEntityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name ==
                    "PhoneNumber");
    }

    private static IdentityDbContext CreateDbContext()
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<
                IdentityDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        return new IdentityDbContext(
            options);
    }
}