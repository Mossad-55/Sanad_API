using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Infrastructure;

[Collection("LocalPostgres")]
public sealed class IdentityPostgresIntegrationTests
{
    private readonly LocalPostgresIdentityFixture _fixture;

    public IdentityPostgresIntegrationTests(
        LocalPostgresIdentityFixture fixture)
    {
        _fixture = fixture;
    }

    [LocalPostgresFact]
    public async Task SaveChanges_ShouldRejectDuplicatePhoneNumber()
    {
        await ResetDatabaseAsync();

        User first =
            CreateUser(
                "first@example.com",
                "+201001234567");

        User second =
            CreateUser(
                "second@example.com",
                "+201001234567");

        _fixture.DbContext.Users.Add(first);

        await _fixture.DbContext.SaveChangesAsync();

        _fixture.DbContext.Users.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _fixture.DbContext.SaveChangesAsync());
    }

    [LocalPostgresFact]
    public async Task SaveChanges_ShouldRejectDuplicateEmail()
    {
        await ResetDatabaseAsync();

        User first =
            CreateUser(
                "same@example.com",
                "+201001234567");

        User second =
            CreateUser(
                "same@example.com",
                "+201009999999");

        _fixture.DbContext.Users.Add(first);

        await _fixture.DbContext.SaveChangesAsync();

        _fixture.DbContext.Users.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _fixture.DbContext.SaveChangesAsync());
    }

    private static User CreateUser(
        string email,
        string phoneNumber)
    {
        User user =
            User.Create(
                FullName.Create("محمد أحمد"),
                FullName.Create("Mohamed Ahmed"),
                Email.Create(email),
                PhoneNumber.Create(phoneNumber));

        user.AddAccount(
            AccountType.Family);

        return user;
    }

    private async Task ResetDatabaseAsync()
    {
        _fixture.DbContext.ChangeTracker.Clear();

        await _fixture.DbContext.Database.EnsureDeletedAsync();

        await _fixture.DbContext.Database.EnsureCreatedAsync();

        _fixture.DbContext.ChangeTracker.Clear();
    }
}