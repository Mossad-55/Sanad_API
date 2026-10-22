using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sanad.UnitTests.Cms;

/// <summary>
/// The global support contact pair is independently nullable. Every case here
/// is a locked decision: no row and both-unset both read as 200 with two null
/// fields (never 404/204/no body), each value can be set or cleared on its own,
/// a malformed populated value still fails validation, and the admin read never
/// creates the row.
/// </summary>
public sealed class SupportContactNullableTests
{
    private const string ValidPhone = "+201000000001";
    private const string ValidEmail = "support@sanad.example";

    [Fact]
    public async Task AdminGet_WithoutAnyRow_Returns200WithTwoNullFields()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        var result = await new GetSupportContactQueryHandler(dbContext).Handle(
            new GetSupportContactQuery(),
            CancellationToken.None);

        // A configuration that was never written is a valid unconfigured
        // state: success with both fields null, not a not-found failure.
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.SupportPhone);
        Assert.Null(result.Value.SupportEmail);

        // The read is non-destructive.
        Assert.Empty(dbContext.SupportContacts);
    }

    [Fact]
    public async Task AdminGet_AfterBothValuesAreCleared_Returns200WithTwoNullFields()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        UpsertSupportContactCommandHandler handler = new(dbContext);

        await handler.Handle(
            new UpsertSupportContactCommand(ValidPhone, ValidEmail),
            CancellationToken.None);

        var cleared = await handler.Handle(
            new UpsertSupportContactCommand(null, null),
            CancellationToken.None);

        Assert.True(cleared.IsSuccess);
        Assert.Null(cleared.Value.SupportPhone);
        Assert.Null(cleared.Value.SupportEmail);

        // The singleton row survives the clear; it is not deleted.
        SupportContact stored = Assert.Single(dbContext.SupportContacts);
        Assert.Null(stored.SupportPhone);
        Assert.Null(stored.SupportEmail);

        var adminRead = await new GetSupportContactQueryHandler(dbContext)
            .Handle(new GetSupportContactQuery(), CancellationToken.None);

        Assert.True(adminRead.IsSuccess);
        Assert.Null(adminRead.Value.SupportPhone);
        Assert.Null(adminRead.Value.SupportEmail);
    }

    [Fact]
    public async Task Upsert_SettingOneField_LeavesTheOtherIndependent()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        UpsertSupportContactCommandHandler handler = new(dbContext);

        // Phone only: the email stays unset.
        var phoneOnly = await handler.Handle(
            new UpsertSupportContactCommand(ValidPhone, null),
            CancellationToken.None);

        Assert.True(phoneOnly.IsSuccess);
        Assert.Equal(ValidPhone, phoneOnly.Value.SupportPhone);
        Assert.Null(phoneOnly.Value.SupportEmail);

        // Adding the email leaves the phone untouched.
        var both = await handler.Handle(
            new UpsertSupportContactCommand(ValidPhone, ValidEmail),
            CancellationToken.None);

        Assert.True(both.IsSuccess);
        Assert.Equal(ValidPhone, both.Value.SupportPhone);
        Assert.Equal(ValidEmail, both.Value.SupportEmail);

        // Clearing the phone keeps the email — this is replacement per field,
        // not a two-field replacement.
        var emailOnly = await handler.Handle(
            new UpsertSupportContactCommand(null, ValidEmail),
            CancellationToken.None);

        Assert.True(emailOnly.IsSuccess);
        Assert.Null(emailOnly.Value.SupportPhone);
        Assert.Equal(ValidEmail, emailOnly.Value.SupportEmail);

        Assert.Single(dbContext.SupportContacts);

        SupportContact stored = dbContext.SupportContacts.Single();
        Assert.Null(stored.SupportPhone);
        Assert.Equal(ValidEmail, stored.SupportEmail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Upsert_BlankValue_IsRejectedByValidation(string blank)
    {
        // Locked contract: blank strings are rejected (400 over HTTP) — only a
        // null clears a field. The validator is the HTTP enforcement point; a
        // handler call alone would bypass it and is unreachable over HTTP.
        var validator = new UpsertSupportContactCommandValidator();

        Assert.False(validator.Validate(
            new UpsertSupportContactCommand(blank, ValidEmail)).IsValid);
        Assert.False(validator.Validate(
            new UpsertSupportContactCommand(ValidPhone, blank)).IsValid);
    }

    [Fact]
    public async Task PublicRead_UnconfiguredAndConfigured_StaysByteCompatible()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        var beforeConfiguration = await new GetPublicSupportContactQueryHandler(
            dbContext).Handle(
            new GetPublicSupportContactQuery(),
            CancellationToken.None);

        Assert.True(beforeConfiguration.IsSuccess);
        Assert.NotNull(beforeConfiguration.Value);
        Assert.Null(beforeConfiguration.Value!.SupportPhone);
        Assert.Null(beforeConfiguration.Value.SupportEmail);

        await new UpsertSupportContactCommandHandler(dbContext).Handle(
            new UpsertSupportContactCommand(ValidPhone, ValidEmail),
            CancellationToken.None);

        var configured = await new GetPublicSupportContactQueryHandler(dbContext)
            .Handle(new GetPublicSupportContactQuery(), CancellationToken.None);

        Assert.True(configured.IsSuccess);
        Assert.NotNull(configured.Value);
        Assert.Equal(ValidPhone, configured.Value!.SupportPhone);
        Assert.Equal(ValidEmail, configured.Value.SupportEmail);
    }

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CmsDbContext(options);
    }
}
