using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class BankTests
{
    [Fact]
    public void Create_ShouldCreateActiveBankWithUpperCode()
    {
        Bank bank = Bank.Create(
            "nbe",
            "البنك الأهلي المصري",
            "National Bank of Egypt");

        Assert.NotEqual(
            BankId.Empty,
            bank.Id);

        Assert.Equal("NBE", bank.Code);
        Assert.Equal("البنك الأهلي المصري", bank.ArabicName);
        Assert.Equal("National Bank of Egypt", bank.EnglishName);
        Assert.True(bank.IsActive);

        Assert.Equal(
            bank.CreatedOnUtc,
            bank.UpdatedOnUtc);
    }

    [Theory]
    [InlineData("NBE", "NBE")]
    [InlineData(" nbe ", "NBE")]
    [InlineData("CIB123", "CIB123")]
    [InlineData("EG0134", "EG0134")]
    public void Create_ShouldNormalizeValidBankCode(
        string code,
        string expectedCode)
    {
        Bank bank = Bank.Create(
            code,
            "بنك",
            "Bank");

        Assert.Equal(
            expectedCode,
            bank.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NB")]
    [InlineData("ABCDEFGHIJKL")]
    [InlineData("NB-1")]
    [InlineData("NB E")]
    [InlineData("بنك")]
    public void Create_ShouldRejectInvalidBankCode(
        string? code)
    {
        Assert.Throws<DomainException>(
            () => Bank.Create(
                code!,
                "البنك الأهلي المصري",
                "National Bank of Egypt"));
    }

    [Theory]
    [InlineData(null, "National Bank of Egypt")]
    [InlineData("", "National Bank of Egypt")]
    [InlineData("البنك الأهلي المصري", null)]
    [InlineData("البنك الأهلي المصري", "")]
    public void Create_ShouldRejectMissingBankName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => Bank.Create(
                "NBE",
                arabicName!,
                englishName!));
    }

    [Fact]
    public void UpdateNames_ShouldTrimNamesAndKeepCode()
    {
        Bank bank = Bank.Create(
            "NBE",
            "البنك الأهلي المصري",
            "National Bank of Egypt");

        bank.UpdateNames(
            "  البنك الأهلي  ",
            "  National Bank  ");

        Assert.Equal("NBE", bank.Code);
        Assert.Equal("البنك الأهلي", bank.ArabicName);
        Assert.Equal("National Bank", bank.EnglishName);
    }

    [Fact]
    public void Activate_ShouldBeIdempotent_WhenAlreadyActive()
    {
        Bank bank = Bank.Create(
            "NBE",
            "البنك الأهلي المصري",
            "National Bank of Egypt");

        DateTime updatedOnUtc = bank.UpdatedOnUtc;

        bank.Activate();

        Assert.True(bank.IsActive);
        Assert.Equal(updatedOnUtc, bank.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldHideBankFromPublicLookup()
    {
        Bank bank = Bank.Create(
            "NBE",
            "البنك الأهلي المصري",
            "National Bank of Egypt");

        bank.Deactivate();

        Assert.False(bank.IsActive);

        bank.Deactivate();

        Assert.False(bank.IsActive);
    }
}
