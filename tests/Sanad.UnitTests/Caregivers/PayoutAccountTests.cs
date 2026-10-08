using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class PayoutAccountTests
{
    [Fact]
    public void Create_ShouldStoreCiphertextAndExposeOnlyLastFour()
    {
        CaregiverPayoutAccount account =
            CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "  Mohamed Ahmed  ",
                "nbe",
                "v1.k1.ciphertext",
                "6819");

        Assert.Equal("Mohamed Ahmed", account.AccountHolderName);
        Assert.Equal("NBE", account.BankCode);
        Assert.Equal("v1.k1.ciphertext", account.IbanCiphertext);
        Assert.Equal("6819", account.IbanLast4);
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
                "v1.k1.ciphertext",
                "3000");

        Assert.Equal("****3000", account.MaskedIban());
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
                "v1.k1.ciphertext",
                "6819"));
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
                "v1.k1.ciphertext",
                "6819"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRejectMissingCiphertext(
        string? ibanCiphertext)
    {
        Assert.Throws<DomainException>(
            () => CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "Mohamed Ahmed",
                "NBE",
                ibanCiphertext!,
                "6819"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("681")]
    [InlineData("68190")]
    public void Create_ShouldRejectInvalidDisplaySuffix(
        string? ibanLast4)
    {
        Assert.Throws<DomainException>(
            () => CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "Mohamed Ahmed",
                "NBE",
                "v1.k1.ciphertext",
                ibanLast4!));
    }

    [Fact]
    public void UpdateDetails_ShouldReplaceCiphertextAndReturnToPending()
    {
        CaregiverPayoutAccount account =
            CaregiverPayoutAccount.Create(
                new CaregiverId(Guid.CreateVersion7()),
                "Mohamed Ahmed",
                "NBE",
                "v1.k1.old",
                "6819");

        account.UpdateDetails(
            "Mohamed A. Hassan",
            "cib",
            "v1.k1.new",
            "3000");

        Assert.Equal("Mohamed A. Hassan", account.AccountHolderName);
        Assert.Equal("CIB", account.BankCode);
        Assert.Equal("v1.k1.new", account.IbanCiphertext);
        Assert.Equal("****3000", account.MaskedIban());
        Assert.Equal(PayoutAccountStatus.Pending, account.Status);
        Assert.Null(account.RejectionReason);
    }
}
