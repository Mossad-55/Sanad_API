using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.SocialLogin;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.SocialLogin;

public sealed class LinkExternalLoginCommandHandlerTests
{
    [Theory]
    [InlineData(UserStatus.Active, AccountType.Family)]
    [InlineData(UserStatus.PendingVerification, AccountType.MedicalCaregiver)]
    public async Task Handle_ShouldLinkEligibleUserWithoutSessionOrLastLoginMutation(
        UserStatus status,
        AccountType accountType)
    {
        await using IdentityTestDbContext db = CreateDb();
        User user = await SeedUserAsync(db, status, accountType);
        DateTime? originalLastLogin = user.LastLoginOnUtc;
        db.ResetSaveChangesCalls();

        Result<LinkExternalLoginResponse> result = await CreateHandler(db, CreateIdentity())
            .Handle(new LinkExternalLoginCommand(user.Id, ExternalLoginProvider.Google, "credential", new string('n', ExternalAuthenticationNoncePolicy.EncodedLength)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal(ExternalLoginProvider.Google, result.Value.Provider);
        Assert.Equal(FixedClock.Now, result.Value.LinkedOnUtc);
        Assert.Equal(status, user.Status);
        Assert.Equal(originalLastLogin, user.LastLoginOnUtc);
        UserExternalLogin login = Assert.Single(user.ExternalLogins);
        Assert.Equal("google-subject", login.ProviderSubject);
        Assert.Empty(db.DeviceSessions);
        Assert.Empty(db.VerificationRequests);
        Assert.Equal(1, db.SaveChangesCalls);
    }

    [Theory]
    [InlineData(UserStatus.Suspended, AccountType.Family)]
    [InlineData(UserStatus.Blocked, AccountType.Family)]
    [InlineData(UserStatus.PendingVerification, AccountType.Elderly)]
    public async Task Handle_ShouldReturnGenericFailure_ForIneligibleUser(
        UserStatus status,
        AccountType accountType)
    {
        await using IdentityTestDbContext db = CreateDb();
        User user = await SeedUserAsync(db, status, accountType);
        db.ResetSaveChangesCalls();

        Result<LinkExternalLoginResponse> result = await CreateHandler(db, CreateIdentity())
            .Handle(new LinkExternalLoginCommand(user.Id, ExternalLoginProvider.Google, "credential", new string('n', ExternalAuthenticationNoncePolicy.EncodedLength)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.ExternalLinkFailed, result.Error);
        Assert.Empty(user.ExternalLogins);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnGenericFailure_WhenUserIsMissingOrCredentialIsInvalid()
    {
        await using IdentityTestDbContext db = CreateDb();
        db.ResetSaveChangesCalls();

        Result<LinkExternalLoginResponse> missingUser = await CreateHandler(db, CreateIdentity())
            .Handle(new LinkExternalLoginCommand(UserId.New(), ExternalLoginProvider.Google, "credential", new string('n', ExternalAuthenticationNoncePolicy.EncodedLength)), CancellationToken.None);

        Result<LinkExternalLoginResponse> invalidCredential = await CreateHandler(db, null)
            .Handle(new LinkExternalLoginCommand(UserId.New(), ExternalLoginProvider.Google, "credential", new string('n', ExternalAuthenticationNoncePolicy.EncodedLength)), CancellationToken.None);

        Assert.False(missingUser.IsSuccess);
        Assert.False(invalidCredential.IsSuccess);
        Assert.Equal(SocialLoginErrors.ExternalLinkFailed, missingUser.Error);
        Assert.Equal(SocialLoginErrors.ExternalLinkFailed, invalidCredential.Error);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ShouldReturnGenericFailure_ForExistingProviderOrSubjectCollision()
    {
        await using IdentityTestDbContext db = CreateDb();
        User caller = await SeedUserAsync(db, UserStatus.Active, AccountType.Family);
        User other = await SeedUserAsync(db, UserStatus.Active, AccountType.Family, "other@example.com", "+201009999999");
        other.LinkExternalLogin(ExternalLoginProvider.Google, "google-subject", FixedClock.Now);
        await db.SaveChangesAsync();
        db.ResetSaveChangesCalls();

        Result<LinkExternalLoginResponse> result = await CreateHandler(db, CreateIdentity())
            .Handle(new LinkExternalLoginCommand(caller.Id, ExternalLoginProvider.Google, "credential", new string('n', ExternalAuthenticationNoncePolicy.EncodedLength)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialLoginErrors.ExternalLinkFailed, result.Error);
        Assert.Empty(caller.ExternalLogins);
        Assert.Single(other.ExternalLogins);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    private static LinkExternalLoginCommandHandler CreateHandler(IdentityTestDbContext db, VerifiedExternalIdentity? identity)
        => new(db, new Verifier(identity), new FixedClock());

    private static IdentityTestDbContext CreateDb() => new(new DbContextOptionsBuilder<IdentityTestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<User> SeedUserAsync(IdentityTestDbContext db, UserStatus status, AccountType accountType,
        string email = "mohamed@example.com", string phone = "+201001234567")
    {
        User user = User.Create(FullName.Create("محمد أحمد"), FullName.Create("Mohamed Ahmed"), Email.Create(email), PhoneNumber.Create(phone));
        user.AddAccount(accountType);
        if (status is UserStatus.Active or UserStatus.Suspended)
        {
            if (accountType != AccountType.Elderly) user.SetInitialPasswordHash("password-hash", FixedClock.Now);
            user.VerifyEmail(FixedClock.Now);
            user.VerifyPhone(FixedClock.Now);
            user.Activate(FixedClock.Now);
        }
        if (status == UserStatus.Suspended) user.Suspend("Security review.", FixedClock.Now);
        if (status == UserStatus.Blocked) user.Block("Security review.", FixedClock.Now);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static VerifiedExternalIdentity CreateIdentity() =>
        new(ExternalLoginProvider.Google,
        "google-subject",
        "mohamed@example.com");

    private sealed class Verifier(
        VerifiedExternalIdentity? identity) :
        IExternalIdentityVerifier
    {
        public Task<VerifiedExternalIdentity?> VerifyAsync(
            ExternalLoginProvider provider,
            ExternalIdentityCredential credential,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                identity);
        }
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        internal static readonly DateTime Now = new(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => Now;
    }
}
