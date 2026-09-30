using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sanad.Modules.Cms.Domain.Welcome;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class ElderlyWelcomeModelTests
{
    [Fact]
    public void Model_MapsWelcomeSchemaTablesAndSingletonStatusIndex()
    {
        using var db = CreateDbContext();

        Assert.Equal(CmsDbContext.Schema, db.Model.GetDefaultSchema());
        var welcome = db.Model.FindEntityType(typeof(ElderlyWelcome))!;
        Assert.Equal("elderly_welcomes", welcome.GetTableName());
        Assert.Contains(welcome.GetIndexes(), index => !index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual([nameof(ElderlyWelcome.Status)]));
        Assert.Equal(ValueGenerated.Never, welcome.FindProperty(nameof(ElderlyWelcome.Id))!.ValueGenerated);

        var benefits = db.Model.FindEntityType(typeof(ElderlyWelcomeBenefit))!;
        Assert.True(benefits.IsOwned());
        Assert.Equal("elderly_welcome_benefits", benefits.GetTableName());
        Assert.NotNull(benefits.FindProperty("ElderlyWelcomeId"));
        Assert.Contains(benefits.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(["ElderlyWelcomeId", nameof(ElderlyWelcomeBenefit.DisplayOrder)]));
    }

    [Fact]
    public void Model_MapsLocalizedFieldsAsRequiredAndBoundedAndStatusAsInt()
    {
        using var db = CreateDbContext();
        var welcome = db.Model.FindEntityType(typeof(ElderlyWelcome))!;
        AssertRequiredBounded(welcome,
        [
            nameof(ElderlyWelcome.ArabicHeadline), nameof(ElderlyWelcome.EnglishHeadline),
            nameof(ElderlyWelcome.ArabicCtaLabel), nameof(ElderlyWelcome.EnglishCtaLabel)
        ]);
        Assert.Equal(typeof(int), welcome.FindProperty(nameof(ElderlyWelcome.Status))!.GetProviderClrType());

        var benefits = db.Model.FindEntityType(typeof(ElderlyWelcomeBenefit))!;
        AssertRequiredBounded(benefits,
        [
            nameof(ElderlyWelcomeBenefit.ArabicTitle), nameof(ElderlyWelcomeBenefit.EnglishTitle),
            nameof(ElderlyWelcomeBenefit.ArabicDescription), nameof(ElderlyWelcomeBenefit.EnglishDescription)
        ]);
    }

    private static void AssertRequiredBounded(IEntityType entityType, IEnumerable<string> propertyNames)
    {
        foreach (var name in propertyNames)
        {
            var property = entityType.FindProperty(name)!;
            Assert.False(property.IsNullable);
            Assert.Equal(ElderlyWelcome.MaximumTextLength, property.GetMaxLength());
        }
    }

    private static CmsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
