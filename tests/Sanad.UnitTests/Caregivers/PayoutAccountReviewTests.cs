using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class PayoutAccountReviewTests
{
    [Fact]
    public void Verify_ShouldMovePendingAccountToVerified()
    {
        CaregiverPayoutAccount account = PendingAccount();
        var actor = UserId.New();

        account.Verify(actor, 1, Utc(2), "BankPortal", "REF-1");

        Assert.Equal(PayoutAccountStatus.Verified, account.Status);
        Assert.Equal(actor, account.VerifiedBy);
        Assert.Equal(Utc(2), account.VerifiedOnUtc);
        Assert.Equal(actor, account.ReviewedBy);
        Assert.Equal("BankPortal", account.VerificationSource);
        Assert.Equal("REF-1", account.Reference);
        Assert.Null(account.RejectionReason);
    }

    [Fact]
    public void Reject_ShouldMovePendingAccountToRejected()
    {
        CaregiverPayoutAccount account = PendingAccount();
        var actor = UserId.New();

        account.Reject(actor, "Name mismatch", 1, Utc(2));

        Assert.Equal(PayoutAccountStatus.Rejected, account.Status);
        Assert.Equal("Name mismatch", account.RejectionReason);
        Assert.Equal(actor, account.ReviewedBy);
        Assert.Equal(Utc(2), account.ReviewedOnUtc);
        Assert.Null(account.VerifiedBy);
    }

    [Fact]
    public void Revoke_ShouldMoveVerifiedAccountToRevoked()
    {
        CaregiverPayoutAccount account = PendingAccount();
        var verifier = UserId.New();
        account.Verify(verifier, 1, Utc(2), "BankPortal", null);
        var revoker = UserId.New();

        account.Revoke(revoker, "Fraud suspected", 1, Utc(3));

        Assert.Equal(PayoutAccountStatus.Revoked, account.Status);
        Assert.Equal("Fraud suspected", account.RejectionReason);
        Assert.Null(account.VerifiedBy);
        Assert.Null(account.VerifiedOnUtc);
        Assert.Equal(revoker, account.ReviewedBy);
    }

    [Theory]
    [InlineData(PayoutAccountStatus.Verified)]
    [InlineData(PayoutAccountStatus.Rejected)]
    [InlineData(PayoutAccountStatus.Revoked)]
    public void Verify_ShouldRejectNonPendingAccount(PayoutAccountStatus status)
    {
        CaregiverPayoutAccount account = AccountInStatus(status);

        Assert.Throws<DomainException>(
            () => account.Verify(UserId.New(), account.Revision, Utc(2), "BankPortal", null));
    }

    [Fact]
    public void Reject_ShouldRejectVerifiedAccount()
    {
        CaregiverPayoutAccount account = PendingAccount();
        account.Verify(UserId.New(), 1, Utc(2), "BankPortal", null);

        Assert.Throws<DomainException>(
            () => account.Reject(UserId.New(), "Late rejection", 1, Utc(3)));
    }

    [Theory]
    [InlineData(PayoutAccountStatus.Pending)]
    [InlineData(PayoutAccountStatus.Rejected)]
    [InlineData(PayoutAccountStatus.Revoked)]
    public void Revoke_ShouldRejectNonVerifiedAccount(PayoutAccountStatus status)
    {
        CaregiverPayoutAccount account = AccountInStatus(status);

        Assert.Throws<DomainException>(
            () => account.Revoke(UserId.New(), "Reason", account.Revision, Utc(3)));
    }

    [Fact]
    public void Verify_ShouldRejectStaleRevision()
    {
        CaregiverPayoutAccount account = PendingAccount();

        Assert.Throws<DomainException>(
            () => account.Verify(UserId.New(), 999, Utc(2), "BankPortal", null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_ShouldRejectMissingSource(string? source)
    {
        CaregiverPayoutAccount account = PendingAccount();

        Assert.Throws<DomainException>(
            () => account.Verify(UserId.New(), 1, Utc(2), source!, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_ShouldRejectMissingReason(string? reason)
    {
        CaregiverPayoutAccount account = PendingAccount();

        Assert.Throws<DomainException>(
            () => account.Reject(UserId.New(), reason!, 1, Utc(2)));
    }

    [Fact]
    public void UpdateDetails_ShouldBumpRevisionAndClearReviewState()
    {
        CaregiverPayoutAccount account = PendingAccount();
        account.Verify(UserId.New(), 1, Utc(2), "BankPortal", "REF-1");

        account.UpdateDetails(
            "Mohamed Ahmed",
            "NBE",
            "v1.k1.new",
            "3000");

        Assert.Equal(2, account.Revision);
        Assert.Equal(PayoutAccountStatus.Pending, account.Status);
        Assert.Null(account.VerifiedBy);
        Assert.Null(account.VerifiedOnUtc);
        Assert.Null(account.ReviewedBy);
        Assert.Null(account.VerificationSource);
        Assert.Null(account.Reference);
        Assert.Null(account.RejectionReason);
    }

    private static CaregiverPayoutAccount PendingAccount() =>
        CaregiverPayoutAccount.Create(
            new CaregiverId(Guid.CreateVersion7()),
            "Mohamed Ahmed",
            "NBE",
            "v1.k1.ciphertext",
            "6819");

    private static CaregiverPayoutAccount AccountInStatus(PayoutAccountStatus status)
    {
        CaregiverPayoutAccount account = PendingAccount();

        switch (status)
        {
            case PayoutAccountStatus.Verified:
                account.Verify(UserId.New(), 1, Utc(2), "BankPortal", null);
                break;
            case PayoutAccountStatus.Rejected:
                account.Reject(UserId.New(), "Reason", 1, Utc(2));
                break;
            case PayoutAccountStatus.Revoked:
                account.Verify(UserId.New(), 1, Utc(2), "BankPortal", null);
                account.Revoke(UserId.New(), "Reason", 1, Utc(3));
                break;
        }

        return account;
    }

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);
}
