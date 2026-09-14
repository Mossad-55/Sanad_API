using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class CmsDbContextModelTests
{
    [Fact]
    public void Model_ShouldUseCmsSchema()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        Assert.Equal(
            CmsDbContext.Schema,
            dbContext.Model.GetDefaultSchema());
    }

    [Fact]
    public void Model_ShouldMapSplashScreensTable()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        bool tableExists =
            dbContext.Model
                .GetEntityTypes()
                .Any(entityType =>
                    entityType.GetTableName() ==
                    "splash_screens");

        Assert.True(
            tableExists);
    }

    [Fact]
    public void Model_ShouldMapUniqueInternalName()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var entityType =
            dbContext.Model.FindEntityType(
                typeof(Sanad.Modules.Cms.Domain.Splash.SplashScreen));

        Assert.NotNull(entityType);

        Assert.Contains(
            entityType!.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Count == 1 &&
                index.Properties[0].Name ==
                    "InternalName");
    }

    [Fact]
    public void Model_ShouldMapLegalAndHelpTables()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        string[] expectedTables =
        [
            "legal_documents",
            "legal_sections",
            "help_faqs",
            "support_contacts"
        ];

        foreach (string table in expectedTables)
        {
            Assert.Contains(
                dbContext.Model.GetEntityTypes(),
                entityType =>
                    entityType.GetTableName() == table);
        }
    }

    [Fact]
    public void Model_ShouldMapUniqueLegalVersionPerDocumentTypeAndAudience()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var entityType =
            dbContext.Model.FindEntityType(
                typeof(LegalDocument));

        Assert.NotNull(entityType);

        Assert.Contains(
            entityType!.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LegalDocument.DocumentType),
                        nameof(LegalDocument.Audience),
                        nameof(LegalDocument.Version)
                    ]));
    }

    [Fact]
    public void Model_ShouldMapUniqueLegalDraftIndexWithFilter()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var entityType =
            dbContext.Model.FindEntityType(
                typeof(LegalDocument));

        Assert.NotNull(entityType);

        // One Draft per (DocumentType, Audience): a filtered unique index.
        Assert.Contains(
            entityType!.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LegalDocument.DocumentType),
                        nameof(LegalDocument.Audience),
                        nameof(LegalDocument.Status)
                    ]) &&
                index.GetFilter() == "status = 1");
    }

    [Fact]
    public void Model_ShouldMapLegalCurrentPublishedReadIndex()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var entityType =
            dbContext.Model.FindEntityType(
                typeof(LegalDocument));

        Assert.NotNull(entityType);

        Assert.Contains(
            entityType!.GetIndexes(),
            index =>
                !index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(LegalDocument.Status),
                        nameof(LegalDocument.DocumentType),
                        nameof(LegalDocument.Audience),
                        nameof(LegalDocument.Version)
                    ]));
    }

    [Fact]
    public void Model_ShouldMapUniqueLegalSectionOrderPerDocument()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var sectionType =
            dbContext.Model.FindEntityType(
                typeof(LegalSection));

        Assert.NotNull(sectionType);
        Assert.Equal(
            "legal_sections",
            sectionType!.GetTableName());

        Assert.Contains(
            sectionType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        "DocumentId",
                        nameof(LegalSection.DisplayOrder)
                    ]));
    }

    [Fact]
    public void Model_ShouldMapLegalEnumsAsIntegerColumns()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var legalDocument =
            dbContext.Model.FindEntityType(
                typeof(LegalDocument));
        Assert.NotNull(legalDocument);

        AssertMappedToIntEnum(
            legalDocument!,
            nameof(LegalDocument.DocumentType));
        AssertMappedToIntEnum(
            legalDocument,
            nameof(LegalDocument.Audience));
        AssertMappedToIntEnum(
            legalDocument,
            nameof(LegalDocument.Status));

        var legalSection =
            dbContext.Model.FindEntityType(
                typeof(LegalSection));
        Assert.NotNull(legalSection);

        AssertMappedToIntEnum(
            legalSection!,
            nameof(LegalSection.SectionType));

        var helpFaq =
            dbContext.Model.FindEntityType(
                typeof(HelpFaq));
        Assert.NotNull(helpFaq);

        AssertMappedToIntEnum(
            helpFaq!,
            nameof(HelpFaq.Audience));
    }

    [Fact]
    public void Model_ShouldMapHelpFaqActiveReadIndex()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var entityType =
            dbContext.Model.FindEntityType(
                typeof(HelpFaq));

        Assert.NotNull(entityType);

        Assert.Contains(
            entityType!.GetIndexes(),
            index =>
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(HelpFaq.Audience),
                        nameof(HelpFaq.IsActive),
                        nameof(HelpFaq.DisplayOrder)
                    ]));
    }

    [Fact]
    public void Model_ShouldMapSupportContactSingletonRow()
    {
        using CmsDbContext dbContext =
            CreateDbContext();

        var entityType =
            dbContext.Model.FindEntityType(
                typeof(SupportContact));

        Assert.NotNull(entityType);
        Assert.Equal(
            "support_contacts",
            entityType!.GetTableName());

        // The int key is app-assigned and fixed, and the check constraint
        // keeps the table a true singleton.
        IProperty keyProperty =
            entityType.FindPrimaryKey()!.Properties.Single();

        Assert.Equal(
            nameof(SupportContact.Id),
            keyProperty.Name);
        Assert.Equal(
            ValueGenerated.Never,
            keyProperty.GetValueGenerated());

        Assert.Contains(
            entityType.GetCheckConstraints(),
            constraint =>
                constraint.Name == "ck_support_contacts_single_row");
    }

    private static void AssertMappedToIntEnum(
        IEntityType entityType,
        string propertyName)
    {
        IProperty property =
            entityType.FindProperty(propertyName)!;

        // HasConversion<int>() pins the provider (database) type to int,
        // so every legal/help enum column stores its numeric value.
        Assert.Equal(
            typeof(int),
            property.GetProviderClrType());
    }

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<
                CmsDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        return new CmsDbContext(
            options);
    }
}
