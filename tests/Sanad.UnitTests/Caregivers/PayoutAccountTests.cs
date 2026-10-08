using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class PayoutAccountTests
{
    [Fact]
    public void Create_ShouldNormalizeDetailsAndStartPending()
    {
        CaregiverPayoutAccount account =
            CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "  Mohamed Ahmed  ",
                "nbe",
                "gb29 nwbk 6016 1331 9268 19");

        Assert.Equal("Mohamed Ahmed", account.AccountHolderName);
        Assert.Equal("NBE", account.BankCode);
        Assert.Equal("GB29NWBK60161331926819", account.Iban);
        Assert.Equal(PayoutAccountStatus.Pending, account.Status);
        Assert.Null(account.RejectionReason);
    }

    [Fact]
    public void MaskedIban_ShouldExposeOnlyLastFourCharacters()
    {
        CaregiverPayoutAccount account =
            CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "Mohamed Ahmed",
                "NBE",
                "DE89370400440532013000");

        Assert.Equal("****3000", account.MaskedIban());
        Assert.DoesNotContain("DE89", account.MaskedIban());
    }

    [Theory]
    [InlineData("GB29NWBK60161331926818")]
    [InlineData("NBE123")]
    [InlineData("123456789012345")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GB29NWBK6016133192681912345678901234567890")]
    public void Create_ShouldRejectInvalidIban(
        string? iban)
    {
        Assert.Throws<DomainException>(
            () => CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "Mohamed Ahmed",
                "NBE",
                iban!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRejectMissingHolderName(
        string? accountHolderName)
    {
        Assert.Throws<DomainException>(
            () => CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                accountHolderName!,
                "NBE",
                "GB29NWBK60161331926819"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRejectMissingBankCode(
        string? bankCode)
    {
        Assert.Throws<DomainException>(
            () => CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "Mohamed Ahmed",
                bankCode!,
                "GB29NWBK60161331926819"));
    }

    [Fact]
    public void UpdateDetails_ShouldReturnAccountToPending()
    {
        CaregiverPayoutAccount account =
            CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "Mohamed Ahmed",
                "NBE",
                "GB29NWBK60161331926819");

        account.UpdateDetails(
            "Mohamed A. Hassan",
            "cib",
            "DE89370400440532013000");

        Assert.Equal("Mohamed A. Hassan", account.AccountHolderName);
        Assert.Equal("CIB", account.BankCode);
        Assert.Equal("DE89370400440532013000", account.Iban);
        Assert.Equal(PayoutAccountStatus.Pending, account.Status);
        Assert.Null(account.RejectionReason);
    }
}
