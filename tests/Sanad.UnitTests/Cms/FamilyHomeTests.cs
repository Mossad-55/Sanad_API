using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

/// <summary>
/// Focused E05 verification for the Family home CMS surface: separate storage,
/// typed content, the locked content/target/active-reference rules, the
/// draft-twin publication lifecycle with history and optimistic concurrency, and
/// the public read's omission rules. The relational cases run the same handlers
/// over SQLite so a query that only works in memory cannot pass.
/// </summary>
public sealed class FamilyHomeTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // Storage shape (table and column names are frozen by the approved manifest)
    // -----------------------------------------------------------------------

    [Fact]
    public void Model_MapsTheTwoFamilyHomeTablesAndTheirFrozenColumns()
    {
        using CmsDbContext db = CreateCmsContext();

        IEntityType? section = db.Model
            .GetEntityTypes()
            .SingleOrDefault(entity => entity.GetTableName() == "family_home_sections");
        IEntityType? history = db.Model
            .GetEntityTypes()
            .SingleOrDefault(entity => entity.GetTableName() == "family_home_section_history");

        Assert.NotNull(section);
        Assert.NotNull(history);

        Assert.Equal(
            [
                "created_on_utc", "display_order", "id", "is_visible", "items_json",
                "kind", "published_on_utc", "source_id", "status", "updated_on_utc",
                "version"
            ],
            section!.GetProperties().Select(property => property.GetColumnName()).Order().ToArray());

        Assert.Equal(
            ["action", "actor_user_id", "created_on_utc", "details", "id", "section_id"],
            history!.GetProperties().Select(property => property.GetColumnName()).Order().ToArray());

        Assert.True(
            section.FindProperty("Version")!.IsConcurrencyToken,
            "The section version must stay the optimistic concurrency token.");
    }

    // -----------------------------------------------------------------------
    // Typed content: no JSON string anywhere in the contract
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AdminResponse_CarriesTypedItems_AndNeverAJsonString()
    {
        await using CmsDbContext db = CreateCmsContext();

        Result<FamilyHomeAdminSectionResponse> created = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("عرض", "Banner", "landing/hero.png", new FamilyHomeTargetRequest("services"))]);

        Assert.True(created.IsSuccess);
        Assert.Equal("banners", created.Value.Kind);
        Assert.Equal("Draft", created.Value.Status);
        Assert.Equal(1, created.Value.Version);
        Assert.Null(created.Value.SourceId);

        FamilyHomeItem item = Assert.Single(created.Value.Items);
        Assert.Equal("عرض", item.ArabicTitle);
        Assert.Equal("Banner", item.EnglishTitle);
        Assert.Equal("landing/hero.png", item.ImageKey);
        Assert.Equal("services", item.Target!.Type);
        Assert.Null(item.Target.ServiceId);

        Assert.DoesNotContain(
            typeof(FamilyHomeAdminSectionResponse).GetProperties(),
            property => property.Name.Contains("Json", StringComparison.Ordinal));

        // The single serializer drops nulls, so the stored payload holds only the
        // fields that were actually supplied.
        FamilyHomeSection stored = await db.FamilyHomeSections.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("null", stored.ItemsJson!, StringComparison.Ordinal);
        Assert.Contains("landing/hero.png", stored.ItemsJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminRead_RoundTripsTypedItemsIncludingTheTarget()
    {
        await using CmsDbContext db = CreateCmsContext();
        Guid serviceId = Guid.NewGuid();

        Result<FamilyHomeAdminSectionResponse> created = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [
                QuickLink("الخدمات", "Services", new FamilyHomeTargetRequest("services"), "icons/catalog.png"),
                QuickLink("طبيب", "Doctor", new FamilyHomeTargetRequest("service", serviceId))
            ]);

        Assert.True(created.IsSuccess);

        Result<FamilyHomeAdminSectionResponse> detail = await new GetFamilyHomeAdminSectionQueryHandler(db).Handle(
            new GetFamilyHomeAdminSectionQuery(created.Value.Id), CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.Equal(2, detail.Value.Items.Count);
        Assert.Equal("icons/catalog.png", detail.Value.Items[0].IconKey);
        Assert.Equal("service", detail.Value.Items[1].Target!.Type);
        Assert.Equal(serviceId, detail.Value.Items[1].Target!.ServiceId);

        Result<FamilyHomeAdminSectionResponse> missing = await new GetFamilyHomeAdminSectionQueryHandler(db).Handle(
            new GetFamilyHomeAdminSectionQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(missing.IsFailure);
        Assert.Equal("Cms.FamilyHome.SectionNotFound", missing.Error.Code);
    }

    [Fact]
    public async Task AdminReads_FailClosedOnUnreadableStoredContent()
    {
        await using CmsDbContext db = CreateCmsContext();

        FamilyHomeSection section = FamilyHomeSection.Create(
            FamilyHomeSectionKind.Banners, "{ this is not json", 0, true);
        await PublishDirectlyAsync(db, section);
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> detail = await new GetFamilyHomeAdminSectionQueryHandler(db).Handle(
            new GetFamilyHomeAdminSectionQuery(section.Id.Value), CancellationToken.None);
        Assert.True(detail.IsFailure);
        Assert.Equal("Cms.FamilyHome.Incomplete", detail.Error.Code);

        Result<FamilyHomeAdminPageResponse> list = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(), CancellationToken.None);
        Assert.True(list.IsFailure);
        Assert.Equal("Cms.FamilyHome.Incomplete", list.Error.Code);

        Result<IReadOnlyList<FamilyHomeSectionPublicItem>> read =
            await new GetPublishedFamilyHomeSectionsQueryHandler(db, caregivers).Handle(
                new GetPublishedFamilyHomeSectionsQuery(), CancellationToken.None);
        Assert.True(read.IsFailure);
        Assert.Equal("Cms.FamilyHome.Incomplete", read.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Locked content rules
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_AcceptsAnEmptyDraftAndAnEmptyItemsList()
    {
        await using CmsDbContext db = CreateCmsContext();

        Result<FamilyHomeAdminSectionResponse> noItems =
            await CreateAsync(db, FamilyHomeSectionKind.Banners, null);
        Result<FamilyHomeAdminSectionResponse> emptyItems =
            await CreateAsync(db, FamilyHomeSectionKind.QuickLinks, []);

        Assert.True(noItems.IsSuccess);
        Assert.Empty(noItems.Value.Items);
        Assert.True(emptyItems.IsSuccess);
        Assert.Empty(emptyItems.Value.Items);
    }

    [Fact]
    public async Task Create_RejectsMoreThanTenItems()
    {
        await using CmsDbContext db = CreateCmsContext();

        List<FamilyHomeItemRequest> eleven = Enumerable
            .Range(1, 11)
            .Select(index => Banner($"عرض {index}", $"Banner {index}", $"landing/{index}.png"))
            .ToList();

        Result<FamilyHomeAdminSectionResponse> result =
            await CreateAsync(db, FamilyHomeSectionKind.Banners, eleven);

        Assert.True(result.IsFailure);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Create_RejectsBilingualPairingAndCapBreaches()
    {
        await using CmsDbContext db = CreateCmsContext();

        Result<FamilyHomeAdminSectionResponse> arabicOnly = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [new FamilyHomeItemRequest(ArabicTitle: "عنوان", ImageKey: "landing/a.png")]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", arabicOnly.Error.Code);

        Result<FamilyHomeAdminSectionResponse> englishOnly = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [new FamilyHomeItemRequest(EnglishTitle: "Title", ImageKey: "landing/a.png")]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", englishOnly.Error.Code);

        Result<FamilyHomeAdminSectionResponse> longTitle = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner(new string('ع', 201), "Title", "landing/a.png")]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", longTitle.Error.Code);

        Result<FamilyHomeAdminSectionResponse> longBody = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [
                new FamilyHomeItemRequest(
                    ArabicTitle: "عنوان",
                    EnglishTitle: "Title",
                    ArabicBody: new string('ب', 4001),
                    EnglishBody: new string('b', 4001),
                    ImageKey: "landing/a.png")
            ]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", longBody.Error.Code);

        Result<FamilyHomeAdminSectionResponse> longKey = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("عنوان", "Title", new string('k', 501))]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", longKey.Error.Code);

        Result<FamilyHomeAdminSectionResponse> atTheCaps = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [
                new FamilyHomeItemRequest(
                    ArabicTitle: new string('ع', 200),
                    EnglishTitle: new string('e', 200),
                    ArabicBody: new string('ب', 4000),
                    EnglishBody: new string('b', 4000),
                    ImageKey: new string('k', 500))
            ]);
        Assert.True(atTheCaps.IsSuccess);
    }

    [Fact]
    public async Task Create_RejectsAnUndefinedKindOrANegativeDisplayOrder()
    {
        await using CmsDbContext db = CreateCmsContext();

        Result<FamilyHomeAdminSectionResponse> undefined = await new CreateFamilyHomeSectionCommandHandler(db).Handle(
            new CreateFamilyHomeSectionCommand((FamilyHomeSectionKind)99, null, 0, true),
            CancellationToken.None);
        Assert.Equal("Cms.FamilyHome.InvalidKind", undefined.Error.Code);

        Result<FamilyHomeAdminSectionResponse> negative = await new CreateFamilyHomeSectionCommandHandler(db).Handle(
            new CreateFamilyHomeSectionCommand(FamilyHomeSectionKind.Banners, null, -1, true),
            CancellationToken.None);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", negative.Error.Code);
    }

    [Fact]
    public async Task FeaturedServices_RejectEditorialOverridesMissingIdsAndRepeats()
    {
        await using CmsDbContext db = CreateCmsContext();
        Guid serviceId = Guid.NewGuid();

        Result<FamilyHomeAdminSectionResponse> overrideTitle = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ArabicTitle: "اسم", EnglishTitle: "Name", ServiceId: serviceId)]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", overrideTitle.Error.Code);

        Result<FamilyHomeAdminSectionResponse> overrideImage = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ImageKey: "landing/a.png", ServiceId: serviceId)]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", overrideImage.Error.Code);

        Result<FamilyHomeAdminSectionResponse> overrideTarget = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(Target: new FamilyHomeTargetRequest("services"), ServiceId: serviceId)]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", overrideTarget.Error.Code);

        Result<FamilyHomeAdminSectionResponse> missingId = await CreateAsync(
            db, FamilyHomeSectionKind.FeaturedServices, [new FamilyHomeItemRequest()]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", missingId.Error.Code);

        Result<FamilyHomeAdminSectionResponse> emptyId = await CreateAsync(
            db, FamilyHomeSectionKind.FeaturedServices, [new FamilyHomeItemRequest(ServiceId: Guid.Empty)]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", emptyId.Error.Code);

        Result<FamilyHomeAdminSectionResponse> repeated = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ServiceId: serviceId), new FamilyHomeItemRequest(ServiceId: serviceId)]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", repeated.Error.Code);
    }

    [Fact]
    public async Task BannersAndQuickLinks_RejectACatalogServiceId()
    {
        await using CmsDbContext db = CreateCmsContext();

        Result<FamilyHomeAdminSectionResponse> banner = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [new FamilyHomeItemRequest(
                ArabicTitle: "ع",
                EnglishTitle: "T",
                ImageKey: "landing/a.png",
                ServiceId: Guid.NewGuid())]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", banner.Error.Code);

        Result<FamilyHomeAdminSectionResponse> quickLink = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [new FamilyHomeItemRequest(ArabicTitle: "ع", EnglishTitle: "T", ServiceId: Guid.NewGuid())]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", quickLink.Error.Code);
    }

    [Fact]
    public async Task NavigationTargets_AreExactlyServicesOrOneServiceId()
    {
        await using CmsDbContext db = CreateCmsContext();
        Guid serviceId = Guid.NewGuid();

        Result<FamilyHomeAdminSectionResponse> servicesWithId = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("services", serviceId))]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", servicesWithId.Error.Code);

        Result<FamilyHomeAdminSectionResponse> serviceWithoutId = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("service"))]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", serviceWithoutId.Error.Code);

        Result<FamilyHomeAdminSectionResponse> serviceWithEmptyId = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("service", Guid.Empty))]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", serviceWithEmptyId.Error.Code);

        Result<FamilyHomeAdminSectionResponse> unsupported = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("https://example.test/x"))]);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", unsupported.Error.Code);

        Result<FamilyHomeAdminSectionResponse> services = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("services"))]);
        Assert.True(services.IsSuccess);

        Result<FamilyHomeAdminSectionResponse> service = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("service", serviceId))]);
        Assert.True(service.IsSuccess);
    }

    [Fact]
    public void KindAndStatusTokens_AreKebabCaseOrEnumNamesButNeverNumbers()
    {
        Assert.Equal("banners", FamilyHomeSectionKind.Banners.ToKindKey());
        Assert.Equal("quick-links", FamilyHomeSectionKind.QuickLinks.ToKindKey());
        Assert.Equal("featured-services", FamilyHomeSectionKind.FeaturedServices.ToKindKey());

        Assert.True(FamilyHomeKindParser.TryParseKind("quick-links", out FamilyHomeSectionKind kebab));
        Assert.Equal(FamilyHomeSectionKind.QuickLinks, kebab);
        Assert.True(FamilyHomeKindParser.TryParseKind("FeaturedServices", out FamilyHomeSectionKind name));
        Assert.Equal(FamilyHomeSectionKind.FeaturedServices, name);
        Assert.False(FamilyHomeKindParser.TryParseKind("2", out _));
        // The enum name itself stays accepted case-insensitively, so an Admin
        // client that echoes the stored enum spelling keeps working.
        Assert.True(FamilyHomeKindParser.TryParseKind("quicklinks", out FamilyHomeSectionKind loose));
        Assert.Equal(FamilyHomeSectionKind.QuickLinks, loose);
        Assert.False(FamilyHomeKindParser.TryParseKind("quick link", out _));
        Assert.False(FamilyHomeKindParser.TryParseKind(null, out _));

        Assert.True(FamilyHomeKindParser.TryParseStatus("published", out FamilyHomeSectionStatus status));
        Assert.Equal(FamilyHomeSectionStatus.Published, status);
        Assert.False(FamilyHomeKindParser.TryParseStatus("2", out _));
        Assert.False(FamilyHomeKindParser.TryParseStatus("hidden", out _));
    }

    // -----------------------------------------------------------------------
    // Publication
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Publish_RequiresPublishedCompleteness()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> empty = await CreateAsync(db, FamilyHomeSectionKind.Banners, null);
        Assert.Equal("Cms.FamilyHome.Incomplete", (await PublishAsync(db, caregivers, empty.Value.Id)).Error.Code);

        Result<FamilyHomeAdminSectionResponse> noImage = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [new FamilyHomeItemRequest(ArabicTitle: "ع", EnglishTitle: "T")]);
        Assert.Equal("Cms.FamilyHome.Incomplete", (await PublishAsync(db, caregivers, noImage.Value.Id)).Error.Code);

        Result<FamilyHomeAdminSectionResponse> noTarget = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [new FamilyHomeItemRequest(ArabicTitle: "ع", EnglishTitle: "T")]);
        Assert.Equal("Cms.FamilyHome.Incomplete", (await PublishAsync(db, caregivers, noTarget.Value.Id)).Error.Code);
    }

    [Fact]
    public async Task Publish_RequiresReferencedCatalogServicesToBeActive()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Service active = SeedService(caregivers, "تمريض", "Nursing", CaregiverType.Medical);
        Service inactive = SeedService(caregivers, "رفقة", "Companionship", CaregiverType.Companion, isActive: false);

        Result<FamilyHomeAdminSectionResponse> activeSection = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ServiceId: active.Id.Value)]);
        Assert.True((await PublishAsync(db, caregivers, activeSection.Value.Id)).IsSuccess);

        Result<FamilyHomeAdminSectionResponse> inactiveSection = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ServiceId: inactive.Id.Value)]);
        Assert.Equal(
            "Cms.FamilyHome.Incomplete",
            (await PublishAsync(db, caregivers, inactiveSection.Value.Id)).Error.Code);

        Result<FamilyHomeAdminSectionResponse> unknownSection = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ServiceId: Guid.NewGuid())]);
        Assert.Equal(
            "Cms.FamilyHome.Incomplete",
            (await PublishAsync(db, caregivers, unknownSection.Value.Id)).Error.Code);

        Result<FamilyHomeAdminSectionResponse> inactiveTarget = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("service", inactive.Id.Value))]);
        Assert.Equal(
            "Cms.FamilyHome.Incomplete",
            (await PublishAsync(db, caregivers, inactiveTarget.Value.Id)).Error.Code);

        Result<FamilyHomeAdminSectionResponse> catalogTarget = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("services"))]);
        Assert.True((await PublishAsync(db, caregivers, catalogTarget.Value.Id)).IsSuccess);
    }

    [Fact]
    public async Task Publish_ArchivesThePreviousPublishedSectionOfTheSameKindAndAppendsHistory()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> first = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("الخدمات", "Services", new FamilyHomeTargetRequest("services"))]);
        Assert.True((await PublishAsync(db, caregivers, first.Value.Id)).IsSuccess);

        Result<FamilyHomeAdminSectionResponse> second = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("الأطباء", "Doctors", new FamilyHomeTargetRequest("services"))]);
        Result<FamilyHomeAdminSectionResponse> republished = await PublishAsync(db, caregivers, second.Value.Id);

        Assert.True(republished.IsSuccess);
        Assert.Equal("Published", republished.Value.Status);
        Assert.NotNull(republished.Value.PublishedOnUtc);

        List<FamilyHomeSection> rows = await db.FamilyHomeSections.AsNoTracking().ToListAsync();
        Assert.Equal(FamilyHomeSectionStatus.Archived, rows.Single(row => row.Id.Value == first.Value.Id).Status);
        Assert.Equal(FamilyHomeSectionStatus.Published, rows.Single(row => row.Id.Value == second.Value.Id).Status);

        List<FamilyHomeSectionHistory> history = await db.FamilyHomeSectionHistory.AsNoTracking().ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, row => Assert.Equal(LandingHistoryAction.Published, row.Action));
        Assert.All(history, row => Assert.Equal(ActorId, row.ActorUserId.Value));
        Assert.Contains(history, row => row.SectionId.Value == first.Value.Id);
    }

    [Fact]
    public async Task Publish_RefusesPublishedArchivedStaleAndAnonymousRequests()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> draft = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("ع", "T", "landing/a.png")]);

        Assert.Equal(
            "Cms.FamilyHome.StaleVersion",
            (await new PublishFamilyHomeSectionCommandHandler(db, caregivers).Handle(
                new PublishFamilyHomeSectionCommand(draft.Value.Id, ActorId, 9), CancellationToken.None)).Error.Code);

        Assert.Equal(
            "Cms.FamilyHome.SectionInvalid",
            (await new PublishFamilyHomeSectionCommandHandler(db, caregivers).Handle(
                new PublishFamilyHomeSectionCommand(draft.Value.Id, ActorId, -1), CancellationToken.None)).Error.Code);

        Assert.Equal(
            "Cms.FamilyHome.SectionInvalid",
            (await new PublishFamilyHomeSectionCommandHandler(db, caregivers).Handle(
                new PublishFamilyHomeSectionCommand(draft.Value.Id, Guid.Empty, 0), CancellationToken.None)).Error.Code);

        Assert.Equal(
            "Cms.FamilyHome.SectionNotFound",
            (await new PublishFamilyHomeSectionCommandHandler(db, caregivers).Handle(
                new PublishFamilyHomeSectionCommand(Guid.NewGuid(), ActorId, 0), CancellationToken.None)).Error.Code);

        Assert.True((await PublishAsync(db, caregivers, draft.Value.Id)).IsSuccess);

        Assert.Equal(
            "Cms.FamilyHome.SectionInvalid",
            (await PublishAsync(db, caregivers, draft.Value.Id)).Error.Code);

        FamilyHomeSection archived = FamilyHomeSection.Create(FamilyHomeSectionKind.Banners, null, 5, true);
        archived.Publish(archived.Version);
        archived.Archive();
        db.FamilyHomeSections.Add(archived);
        await db.SaveChangesAsync();

        Assert.Equal(
            "Cms.FamilyHome.ImmutableHistory",
            (await PublishAsync(db, caregivers, archived.Id.Value)).Error.Code);
    }

    // -----------------------------------------------------------------------
    // Draft twin, concurrency and history
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_PublishedRow_CreatesThenReusesItsDraftTwin()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> live = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("قديم", "Old", "landing/old.png")]);
        Assert.True((await PublishAsync(db, caregivers, live.Value.Id)).IsSuccess);

        Result<FamilyHomeAdminSectionResponse> twin = await UpdateAsync(
            db, live.Value.Id, [Banner("جديد", "New", "landing/new.png")], displayOrder: 2, expectedVersion: 0);

        Assert.True(twin.IsSuccess);
        Assert.NotEqual(live.Value.Id, twin.Value.Id);
        Assert.Equal(live.Value.Id, twin.Value.SourceId);
        Assert.Equal("Draft", twin.Value.Status);
        Assert.Equal("New", Assert.Single(twin.Value.Items).EnglishTitle);

        Result<FamilyHomeAdminSectionResponse> second = await UpdateAsync(
            db, live.Value.Id, [Banner("أحدث", "Newest", "landing/newest.png")], displayOrder: 3, expectedVersion: 0);

        Assert.True(second.IsSuccess);
        Assert.Equal(twin.Value.Id, second.Value.Id);
        Assert.Equal("Newest", Assert.Single(second.Value.Items).EnglishTitle);

        // The precondition names the row being edited: the twin when it exists.
        Result<FamilyHomeAdminSectionResponse> stale = await UpdateAsync(
            db,
            live.Value.Id,
            [Banner("متأخر", "Stale", "landing/stale.png")],
            displayOrder: 3,
            expectedVersion: second.Value.Version + 5);
        Assert.Equal("Cms.FamilyHome.StaleVersion", stale.Error.Code);

        List<FamilyHomeSection> rows = await db.FamilyHomeSections.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(FamilyHomeSectionStatus.Published, rows.Single(row => row.Id.Value == live.Value.Id).Status);
    }

    [Fact]
    public async Task Update_Draft_UsesTheRealVersionAndRefusesArchivedRows()
    {
        await using CmsDbContext db = CreateCmsContext();

        Result<FamilyHomeAdminSectionResponse> draft = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("services"))]);

        Result<FamilyHomeAdminSectionResponse> updated = await UpdateAsync(
            db,
            draft.Value.Id,
            [QuickLink("ع2", "T2", new FamilyHomeTargetRequest("services"))],
            displayOrder: 1,
            expectedVersion: draft.Value.Version);
        Assert.True(updated.IsSuccess);
        Assert.Equal(draft.Value.Version + 1, updated.Value.Version);

        Result<FamilyHomeAdminSectionResponse> stale = await UpdateAsync(
            db,
            draft.Value.Id,
            [QuickLink("ع3", "T3", new FamilyHomeTargetRequest("services"))],
            displayOrder: 1,
            expectedVersion: draft.Value.Version);
        Assert.Equal("Cms.FamilyHome.StaleVersion", stale.Error.Code);

        Result<FamilyHomeAdminSectionResponse> negative = await UpdateAsync(
            db,
            draft.Value.Id,
            [QuickLink("ع4", "T4", new FamilyHomeTargetRequest("services"))],
            displayOrder: 1,
            expectedVersion: -1);
        Assert.Equal("Cms.FamilyHome.SectionInvalid", negative.Error.Code);

        Result<FamilyHomeAdminSectionResponse> missing = await UpdateAsync(
            db,
            Guid.NewGuid(),
            [QuickLink("ع5", "T5", new FamilyHomeTargetRequest("services"))],
            displayOrder: 1,
            expectedVersion: 0);
        Assert.Equal("Cms.FamilyHome.SectionNotFound", missing.Error.Code);

        FamilyHomeSection archived = FamilyHomeSection.Create(FamilyHomeSectionKind.Banners, null, 0, true);
        archived.Publish(archived.Version);
        archived.Archive();
        db.FamilyHomeSections.Add(archived);
        await db.SaveChangesAsync();

        Result<FamilyHomeAdminSectionResponse> archivedUpdate = await UpdateAsync(
            db, archived.Id.Value, [Banner("ع", "T", "landing/a.png")], displayOrder: 0, expectedVersion: 0);
        Assert.Equal("Cms.FamilyHome.ImmutableHistory", archivedUpdate.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Deletion semantics
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Delete_RetainsPublishedAndArchivedRowsAndOnlyDraftsAppendHistory()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> draft = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ع", "T", new FamilyHomeTargetRequest("services"))]);
        Result<FamilyHomeAdminSectionResponse> published = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("ع", "T", "landing/a.png")]);
        Assert.True((await PublishAsync(db, caregivers, published.Value.Id)).IsSuccess);

        Assert.True((await DeleteAsync(db, draft.Value.Id)).IsSuccess);

        List<FamilyHomeSectionHistory> history = await db.FamilyHomeSectionHistory.AsNoTracking().ToListAsync();
        FamilyHomeSectionHistory deletion = Assert.Single(
            history,
            row => row.Action == LandingHistoryAction.Deleted);
        Assert.Equal(draft.Value.Id, deletion.SectionId.Value);
        Assert.Equal(ActorId, deletion.ActorUserId.Value);
        Assert.Contains("quick-links", deletion.Details ?? string.Empty, StringComparison.Ordinal);

        Assert.Equal(
            "Cms.FamilyHome.PublishedHistoryConflict",
            (await DeleteAsync(db, published.Value.Id)).Error.Code);

        FamilyHomeSection archived = FamilyHomeSection.Create(FamilyHomeSectionKind.Banners, null, 9, true);
        archived.Publish(archived.Version);
        archived.Archive();
        db.FamilyHomeSections.Add(archived);
        await db.SaveChangesAsync();

        Assert.Equal(
            "Cms.FamilyHome.PublishedHistoryConflict",
            (await DeleteAsync(db, archived.Id.Value)).Error.Code);

        Assert.Equal(
            "Cms.FamilyHome.SectionNotFound",
            (await DeleteAsync(db, Guid.NewGuid())).Error.Code);

        // A refused deletion writes no history row, and the retained rows stay.
        Assert.Equal(2, await db.FamilyHomeSectionHistory.AsNoTracking().CountAsync());
        Assert.Equal(2, await db.FamilyHomeSections.AsNoTracking().CountAsync());

        Assert.Equal(
            "Cms.FamilyHome.SectionInvalid",
            (await new DeleteFamilyHomeSectionCommandHandler(db).Handle(
                new DeleteFamilyHomeSectionCommand(published.Value.Id, Guid.Empty),
                CancellationToken.None)).Error.Code);
    }

    [Fact]
    public async Task Delete_DraftTwin_NeverTouchesItsPublishedSource()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> live = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("قديم", "Old", "landing/old.png")]);
        Assert.True((await PublishAsync(db, caregivers, live.Value.Id)).IsSuccess);

        Result<FamilyHomeAdminSectionResponse> twin = await UpdateAsync(
            db, live.Value.Id, [Banner("جديد", "New", "landing/new.png")], displayOrder: 0, expectedVersion: 0);
        Assert.True(twin.IsSuccess);

        Assert.True((await DeleteAsync(db, twin.Value.Id)).IsSuccess);

        List<FamilyHomeSection> rows = await db.FamilyHomeSections.AsNoTracking().ToListAsync();
        FamilyHomeSection published = Assert.Single(rows);
        Assert.Equal(live.Value.Id, published.Id.Value);
        Assert.Equal(FamilyHomeSectionStatus.Published, published.Status);

        FamilyHomeSectionHistory deletion = Assert.Single(
            await db.FamilyHomeSectionHistory.AsNoTracking().ToListAsync(),
            row => row.Action == LandingHistoryAction.Deleted);
        Assert.Contains("banners", deletion.Details ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(live.Value.Id.ToString(), deletion.Details ?? string.Empty, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Visibility and order
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Visibility_TogglesDraftsAndPublishedRowsAndRefusesArchivedOnes()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> published = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("ع", "T", "landing/a.png")]);
        Assert.True((await PublishAsync(db, caregivers, published.Value.Id)).IsSuccess);

        Assert.True((await new SetFamilyHomeSectionVisibilityCommandHandler(db).Handle(
            new SetFamilyHomeSectionVisibilityCommand(published.Value.Id, false, ActorId, 0),
            CancellationToken.None)).IsSuccess);
        Assert.Empty(await ReadAsync(db, caregivers));

        Assert.True((await new SetFamilyHomeSectionVisibilityCommandHandler(db).Handle(
            new SetFamilyHomeSectionVisibilityCommand(published.Value.Id, true, ActorId, 0),
            CancellationToken.None)).IsSuccess);
        Assert.Single(await ReadAsync(db, caregivers));

        Assert.Contains(
            await db.FamilyHomeSectionHistory.AsNoTracking().ToListAsync(),
            row => row.Action == LandingHistoryAction.VisibilityChanged && row.Details == "IsVisible=False");

        Assert.Equal(
            "Cms.FamilyHome.StaleVersion",
            (await new SetFamilyHomeSectionVisibilityCommandHandler(db).Handle(
                new SetFamilyHomeSectionVisibilityCommand(published.Value.Id, false, ActorId, 99),
                CancellationToken.None)).Error.Code);

        Assert.Equal(
            "Cms.FamilyHome.SectionNotFound",
            (await new SetFamilyHomeSectionVisibilityCommandHandler(db).Handle(
                new SetFamilyHomeSectionVisibilityCommand(Guid.NewGuid(), false, ActorId, 0),
                CancellationToken.None)).Error.Code);

        FamilyHomeSection archived = FamilyHomeSection.Create(FamilyHomeSectionKind.Banners, null, 0, true);
        archived.Publish(archived.Version);
        archived.Archive();
        db.FamilyHomeSections.Add(archived);
        await db.SaveChangesAsync();

        Assert.Equal(
            "Cms.FamilyHome.ImmutableHistory",
            (await new SetFamilyHomeSectionVisibilityCommandHandler(db).Handle(
                new SetFamilyHomeSectionVisibilityCommand(archived.Id.Value, false, ActorId, 0),
                CancellationToken.None)).Error.Code);
    }

    [Fact]
    public async Task Reorder_RewritesTheReadOrderAndBumpsVersions()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> first = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("أول", "First", "landing/1.png")],
            displayOrder: 0);
        Result<FamilyHomeAdminSectionResponse> second = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ثانٍ", "Second", new FamilyHomeTargetRequest("services"))],
            displayOrder: 1);
        Assert.True((await PublishAsync(db, caregivers, first.Value.Id)).IsSuccess);
        Assert.True((await PublishAsync(db, caregivers, second.Value.Id)).IsSuccess);

        Assert.True((await new ReorderFamilyHomeSectionsCommandHandler(db).Handle(
            new ReorderFamilyHomeSectionsCommand(
            [
                new FamilyHomeReorderItem(first.Value.Id, 5),
                new FamilyHomeReorderItem(second.Value.Id, 2)
            ]),
            CancellationToken.None)).IsSuccess);

        List<FamilyHomeSectionPublicItem> read = await ReadAsync(db, caregivers);
        Assert.Equal(2, read.Count);
        Assert.Equal(second.Value.Id, read[0].Id);
        Assert.Equal(2, read[0].DisplayOrder);
        Assert.Equal(first.Value.Id, read[1].Id);

        List<FamilyHomeSection> rows = await db.FamilyHomeSections.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Single(row => row.Id.Value == first.Value.Id).Version);
        Assert.Equal(3, rows.Single(row => row.Id.Value == second.Value.Id).Version);
    }

    [Fact]
    public async Task Reorder_RefusesEmptyDuplicateUnknownAndNegativeEntries()
    {
        await using CmsDbContext db = CreateCmsContext();

        Result<FamilyHomeAdminSectionResponse> section = await CreateAsync(db, FamilyHomeSectionKind.Banners, null);

        Assert.Equal("Cms.FamilyHome.ReorderInvalid", (await ReorderAsync(db, [])).Error.Code);

        Assert.Equal(
            "Cms.FamilyHome.ReorderInvalid",
            (await ReorderAsync(db,
            [
                new FamilyHomeReorderItem(section.Value.Id, 0),
                new FamilyHomeReorderItem(section.Value.Id, 1)
            ])).Error.Code);

        Assert.Equal(
            "Cms.FamilyHome.ReorderInvalid",
            (await ReorderAsync(db, [new FamilyHomeReorderItem(Guid.NewGuid(), 0)])).Error.Code);

        Assert.Equal(
            "Cms.FamilyHome.ReorderInvalid",
            (await ReorderAsync(db, [new FamilyHomeReorderItem(section.Value.Id, -1)])).Error.Code);
    }

    // -----------------------------------------------------------------------
    // Admin list: paging, filters and search
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AdminList_ReturnsPagePageSizeAndTotalCountWithNormalization()
    {
        await using CmsDbContext db = CreateCmsContext();

        for (int index = 0; index < 3; index++)
            await CreateAsync(db, FamilyHomeSectionKind.Banners, null, displayOrder: index);

        Result<FamilyHomeAdminPageResponse> firstPage = await ListAsync(db, page: 1, pageSize: 2);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(3, firstPage.Value.TotalCount);
        Assert.Equal(1, firstPage.Value.Page);
        Assert.Equal(2, firstPage.Value.PageSize);
        Assert.Equal(2, firstPage.Value.Items.Count);

        Result<FamilyHomeAdminPageResponse> secondPage = await ListAsync(db, page: 2, pageSize: 2);
        Assert.True(secondPage.IsSuccess);
        Assert.Single(secondPage.Value.Items);

        Result<FamilyHomeAdminPageResponse> normalized = await ListAsync(db, page: -5, pageSize: 500);
        Assert.True(normalized.IsSuccess);
        Assert.Equal(1, normalized.Value.Page);
        Assert.Equal(100, normalized.Value.PageSize);
        Assert.Equal(3, normalized.Value.Items.Count);

        Result<FamilyHomeAdminPageResponse> minimal = await ListAsync(db, page: 0, pageSize: 0);
        Assert.True(minimal.IsSuccess);
        Assert.Equal(1, minimal.Value.Page);
        Assert.Equal(1, minimal.Value.PageSize);
        Assert.Single(minimal.Value.Items);

        Result<FamilyHomeAdminPageResponse> beyondTheEnd = await ListAsync(db, page: 99, pageSize: 20);
        Assert.True(beyondTheEnd.IsSuccess);
        Assert.Empty(beyondTheEnd.Value.Items);
        Assert.Equal(3, beyondTheEnd.Value.TotalCount);
    }

    [Fact]
    public async Task AdminList_FiltersByStatusAndKindAndSearchesStoredItems()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> banners = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("مرحبا", "Welcome", "landing/a.png")]);
        Result<FamilyHomeAdminSectionResponse> quickLinks = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("الخدمات", "Services", new FamilyHomeTargetRequest("services"))]);
        Assert.True((await PublishAsync(db, caregivers, quickLinks.Value.Id)).IsSuccess);

        Result<FamilyHomeAdminPageResponse> drafts = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(Status: FamilyHomeSectionStatus.Draft), CancellationToken.None);
        Assert.True(drafts.IsSuccess);
        Assert.Equal(banners.Value.Id, Assert.Single(drafts.Value.Items).Id);

        Result<FamilyHomeAdminPageResponse> published = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(Status: FamilyHomeSectionStatus.Published), CancellationToken.None);
        Assert.True(published.IsSuccess);
        Assert.Equal(quickLinks.Value.Id, Assert.Single(published.Value.Items).Id);

        Result<FamilyHomeAdminPageResponse> byKind = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(Kind: FamilyHomeSectionKind.Banners), CancellationToken.None);
        Assert.True(byKind.IsSuccess);
        Assert.Equal(banners.Value.Id, Assert.Single(byKind.Value.Items).Id);

        Result<FamilyHomeAdminPageResponse> searchArabic = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(Search: "مرحبا"), CancellationToken.None);
        Assert.True(searchArabic.IsSuccess);
        Assert.Equal(banners.Value.Id, Assert.Single(searchArabic.Value.Items).Id);

        // The search predicate compares stored text, so Arabic has to be stored
        // literally rather than as \uXXXX escapes.
        string? stored = await db.FamilyHomeSections.AsNoTracking()
            .Where(section => section.Id == new FamilyHomeSectionId(banners.Value.Id))
            .Select(section => section.ItemsJson)
            .SingleAsync();
        Assert.NotNull(stored);
        Assert.Contains("مرحبا", stored!, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0645", stored!, StringComparison.Ordinal);

        Result<FamilyHomeAdminPageResponse> searchEnglish = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(Search: "services"), CancellationToken.None);
        Assert.True(searchEnglish.IsSuccess);
        Assert.Equal(quickLinks.Value.Id, Assert.Single(searchEnglish.Value.Items).Id);

        Result<FamilyHomeAdminPageResponse> noMatch = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(Search: "nothing-matches-this"), CancellationToken.None);
        Assert.True(noMatch.IsSuccess);
        Assert.Empty(noMatch.Value.Items);
        Assert.Equal(0, noMatch.Value.TotalCount);
    }

    [Fact]
    public async Task AdminList_OrdersByDisplayOrderThenCreatedThenId()
    {
        await using CmsDbContext db = CreateCmsContext();

        Result<FamilyHomeAdminSectionResponse> later =
            await CreateAsync(db, FamilyHomeSectionKind.Banners, null, displayOrder: 1);
        Result<FamilyHomeAdminSectionResponse> earlierA =
            await CreateAsync(db, FamilyHomeSectionKind.QuickLinks, null, displayOrder: 0);
        Result<FamilyHomeAdminSectionResponse> earlierB =
            await CreateAsync(db, FamilyHomeSectionKind.FeaturedServices, null, displayOrder: 0);

        Result<FamilyHomeAdminPageResponse> list = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(), CancellationToken.None);

        Assert.True(list.IsSuccess);
        Assert.Equal(3, list.Value.Items.Count);
        Assert.Equal(earlierA.Value.Id, list.Value.Items[0].Id);
        Assert.Equal(earlierB.Value.Id, list.Value.Items[1].Id);
        Assert.Equal(later.Value.Id, list.Value.Items[2].Id);
    }

    // -----------------------------------------------------------------------
    // Public read
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PublicRead_EmitsOnlyPublishedVisibleSectionsInOrder()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Result<FamilyHomeAdminSectionResponse> draft = await CreateAsync(
            db, FamilyHomeSectionKind.Banners, [Banner("مسودة", "Draft", "landing/d.png")], displayOrder: 0);
        Result<FamilyHomeAdminSectionResponse> first = await CreateAsync(
            db, FamilyHomeSectionKind.Banners, [Banner("أول", "First", "landing/1.png")], displayOrder: 1);
        Result<FamilyHomeAdminSectionResponse> second = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("ثانٍ", "Second", new FamilyHomeTargetRequest("services"))],
            displayOrder: 2);
        Assert.True(draft.IsSuccess);
        Assert.True((await PublishAsync(db, caregivers, first.Value.Id)).IsSuccess);
        Assert.True((await PublishAsync(db, caregivers, second.Value.Id)).IsSuccess);

        List<FamilyHomeSectionPublicItem> read = await ReadAsync(db, caregivers);
        Assert.Equal(2, read.Count);
        Assert.Equal(first.Value.Id, read[0].Id);
        Assert.Equal("banners", read[0].Kind);
        Assert.Equal(second.Value.Id, read[1].Id);
        Assert.Equal("quick-links", read[1].Kind);

        // A published section with no remaining items is omitted, and archived
        // content is never emitted.
        FamilyHomeSection emptyOrphan = FamilyHomeSection.Create(FamilyHomeSectionKind.Banners, null, 3, true);
        await PublishDirectlyAsync(db, emptyOrphan);

        Result<FamilyHomeAdminSectionResponse> toArchive = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("مؤرشف", "Archived", new FamilyHomeTargetRequest("services"))],
            displayOrder: 4);
        FamilyHomeSection archived = await GetAsync(db, toArchive.Value.Id);
        archived.Publish(archived.Version);
        archived.Archive();
        await db.SaveChangesAsync();

        Assert.Equal(2, (await ReadAsync(db, caregivers)).Count);
    }

    [Fact]
    public async Task PublicRead_ReturnsAnEmptyListWhenNothingIsPublished()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Assert.Empty(await ReadAsync(db, caregivers));
    }

    [Fact]
    public async Task PublicRead_OmitsUnavailableFeaturedItemsAndTheirSection()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Service active = SeedService(caregivers, "تمريض", "Nursing", CaregiverType.Medical);
        Service inactive = SeedService(caregivers, "رفقة", "Companionship", CaregiverType.Companion, isActive: false);

        Result<FamilyHomeAdminSectionResponse> section = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [
                new FamilyHomeItemRequest(ServiceId: active.Id.Value),
                new FamilyHomeItemRequest(ServiceId: inactive.Id.Value),
                new FamilyHomeItemRequest(ServiceId: Guid.NewGuid())
            ],
            displayOrder: 0);

        // Publication validation already refuses the inactive reference, so the
        // read-omission rule is proved on a published row that held it.
        FamilyHomeSection publishedRow = await GetAsync(db, section.Value.Id);
        publishedRow.Publish(publishedRow.Version);
        await db.SaveChangesAsync();

        FamilyHomeSectionPublicItem published = Assert.Single(await ReadAsync(db, caregivers));
        Assert.Equal("featured-services", published.Kind);

        FamilyHomePublicItem live = Assert.Single(published.Items);
        Assert.Equal(active.Id.Value, live.ServiceId);
        Assert.Equal("service", live.TargetType);
        Assert.Equal(active.Id.Value, live.TargetServiceId);
        Assert.Equal("Nursing", live.ServiceNameEnglish);
        Assert.Equal("تمريض", live.ServiceNameArabic);
        Assert.Equal("icons/svc.png", live.ServiceIconKey);
        Assert.Equal((int)CaregiverType.Medical, live.ServiceCaregiverType);
        Assert.Null(live.ArabicTitle);
        Assert.Null(live.ImageKey);

        Result<FamilyHomeAdminSectionResponse> onlyUnavailable = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ServiceId: inactive.Id.Value)],
            displayOrder: 1);
        FamilyHomeSection orphan = await GetAsync(db, onlyUnavailable.Value.Id);
        orphan.Publish(orphan.Version);
        await db.SaveChangesAsync();

        // Every item was omitted, so the section is omitted with them.
        Assert.Equal(active.Id.Value, Assert.Single(Assert.Single(await ReadAsync(db, caregivers)).Items).ServiceId);
    }

    [Fact]
    public async Task PublicRead_OmitsActionBearingItemsWithUnavailableTargets()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Service active = SeedService(caregivers, "تمريض", "Nursing", CaregiverType.Medical);
        Service inactive = SeedService(caregivers, "رفقة", "Companionship", CaregiverType.Companion, isActive: false);

        Result<FamilyHomeAdminSectionResponse> section = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [
                QuickLink("الكتالوج", "Catalog", new FamilyHomeTargetRequest("services")),
                QuickLink("طبيب", "Doctor", new FamilyHomeTargetRequest("service", active.Id.Value)),
                QuickLink("غير متاح", "Unavailable", new FamilyHomeTargetRequest("service", inactive.Id.Value)),
                QuickLink("مفقود", "Missing", new FamilyHomeTargetRequest("service", Guid.NewGuid()))
            ]);

        FamilyHomeSection publishedRow = await GetAsync(db, section.Value.Id);
        publishedRow.Publish(publishedRow.Version);
        await db.SaveChangesAsync();

        FamilyHomeSectionPublicItem published = Assert.Single(await ReadAsync(db, caregivers));
        Assert.Equal(2, published.Items.Count);
        Assert.Equal("services", published.Items[0].TargetType);
        Assert.Null(published.Items[0].TargetServiceId);
        Assert.Equal("service", published.Items[1].TargetType);
        Assert.Equal(active.Id.Value, published.Items[1].TargetServiceId);
        Assert.Null(published.Items[1].ServiceId);
    }

    [Fact]
    public async Task PublicRead_OmitsAReferenceThatBecomesInactiveLater()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();

        Service service = SeedService(caregivers, "تمريض", "Nursing", CaregiverType.Medical);

        Result<FamilyHomeAdminSectionResponse> section = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ServiceId: service.Id.Value)]);
        Assert.True((await PublishAsync(db, caregivers, section.Value.Id)).IsSuccess);
        Assert.Single(await ReadAsync(db, caregivers));

        service.Deactivate();
        await caregivers.SaveChangesAsync();

        // A reference that becomes inactive later omits the affected item, and a
        // section with no remaining items is omitted entirely - never a whole
        // response failure.
        Assert.Empty(await ReadAsync(db, caregivers));
    }

    [Fact]
    public async Task PublishedFamilyHomeSections_DoNotAppearOnThePublicLandingRead()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using CaregiversDbContext caregivers = CreateCaregiversContext();
        await using FamiliesDbContext families = CreateFamiliesContext();

        Result<FamilyHomeAdminSectionResponse> section = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("رئيسية", "Home", "landing/a.png")]);
        Assert.True((await PublishAsync(db, caregivers, section.Value.Id)).IsSuccess);

        Result<IReadOnlyList<LandingSectionPublicItem>> landing =
            await new GetPublishedLandingSectionsQueryHandler(db, families, caregivers).Handle(
                new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(landing.IsSuccess);
        Assert.Empty(landing.Value);

        // ... and the anonymous Landing surface keeps reading nothing at all from
        // the family home storage.
        Assert.Single(await ReadAsync(db, caregivers));
    }

    // -----------------------------------------------------------------------
    // Relational translation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Relational_AdminListAndFiltersTranslate()
    {
        await using CmsDbContext db = await CreateSqliteCmsContextAsync();
        await using CaregiversDbContext caregivers = await CreateSqliteCaregiversContextAsync();

        Result<FamilyHomeAdminSectionResponse> banners = await CreateAsync(
            db,
            FamilyHomeSectionKind.Banners,
            [Banner("مرحبا", "Welcome", "landing/a.png")]);
        Result<FamilyHomeAdminSectionResponse> quickLinks = await CreateAsync(
            db,
            FamilyHomeSectionKind.QuickLinks,
            [QuickLink("الخدمات", "Services", new FamilyHomeTargetRequest("services"))],
            displayOrder: 1);
        Assert.True((await PublishAsync(db, caregivers, quickLinks.Value.Id)).IsSuccess);

        Result<FamilyHomeAdminPageResponse> all = await ListAsync(db, page: 1, pageSize: 20);
        Assert.True(all.IsSuccess);
        Assert.Equal(2, all.Value.TotalCount);

        Result<FamilyHomeAdminPageResponse> search = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(Search: "welcome"), CancellationToken.None);
        Assert.True(search.IsSuccess);
        Assert.Equal(banners.Value.Id, Assert.Single(search.Value.Items).Id);

        Result<FamilyHomeAdminPageResponse> filtered = await new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(
                Status: FamilyHomeSectionStatus.Published,
                Kind: FamilyHomeSectionKind.QuickLinks),
            CancellationToken.None);
        Assert.True(filtered.IsSuccess);
        Assert.Equal(quickLinks.Value.Id, Assert.Single(filtered.Value.Items).Id);
    }

    [Fact]
    public async Task Relational_CommandsAndTypedReadsTranslate()
    {
        await using CmsDbContext db = await CreateSqliteCmsContextAsync();
        await using CaregiversDbContext caregivers = await CreateSqliteCaregiversContextAsync();

        Service service = SeedService(caregivers, "تمريض", "Nursing", CaregiverType.Medical);

        Result<FamilyHomeAdminSectionResponse> section = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [new FamilyHomeItemRequest(ServiceId: service.Id.Value)]);
        Assert.True(section.IsSuccess);
        Assert.True((await PublishAsync(db, caregivers, section.Value.Id)).IsSuccess);

        Result<FamilyHomeAdminSectionResponse> twin = await UpdateAsync(
            db,
            section.Value.Id,
            [new FamilyHomeItemRequest(ServiceId: service.Id.Value)],
            displayOrder: 2,
            expectedVersion: 0);
        Assert.True(twin.IsSuccess);
        Assert.Equal("Draft", twin.Value.Status);

        Assert.True((await PublishAsync(db, caregivers, twin.Value.Id)).IsSuccess);
        Assert.Equal("Cms.FamilyHome.PublishedHistoryConflict", (await DeleteAsync(db, section.Value.Id)).Error.Code);
        Assert.True((await ReorderAsync(db, [new FamilyHomeReorderItem(twin.Value.Id, 7)])).IsSuccess);

        FamilyHomeSectionPublicItem live = Assert.Single(await ReadAsync(db, caregivers));
        Assert.Equal(7, live.DisplayOrder);
        Assert.Equal(service.Id.Value, Assert.Single(live.Items).ServiceId);

        Result<FamilyHomeAdminSectionResponse> detail = await new GetFamilyHomeAdminSectionQueryHandler(db).Handle(
            new GetFamilyHomeAdminSectionQuery(twin.Value.Id), CancellationToken.None);
        Assert.True(detail.IsSuccess);
        Assert.Equal(service.Id.Value, Assert.Single(detail.Value.Items).ServiceId);
    }

    [Fact]
    public async Task Relational_PublicReadBatchesTheCatalogLookup()
    {
        await using CmsDbContext db = await CreateSqliteCmsContextAsync();
        await using CaregiversDbContext caregivers = await CreateSqliteCaregiversContextAsync();

        Service medical = SeedService(caregivers, "تمريض", "Nursing", CaregiverType.Medical);
        Service companion = SeedService(caregivers, "رفقة", "Companionship", CaregiverType.Companion);
        SeedService(caregivers, "غير مفعّل", "Inactive", CaregiverType.Medical, isActive: false);

        Result<FamilyHomeAdminSectionResponse> section = await CreateAsync(
            db,
            FamilyHomeSectionKind.FeaturedServices,
            [
                new FamilyHomeItemRequest(ServiceId: medical.Id.Value),
                new FamilyHomeItemRequest(ServiceId: companion.Id.Value)
            ]);
        Assert.True((await PublishAsync(db, caregivers, section.Value.Id)).IsSuccess);

        FamilyHomeSectionPublicItem published = Assert.Single(await ReadAsync(db, caregivers));
        Assert.Equal(2, published.Items.Count);
        Assert.Equal((int)CaregiverType.Medical, published.Items[0].ServiceCaregiverType);
        Assert.Equal((int)CaregiverType.Companion, published.Items[1].ServiceCaregiverType);
    }

    // -----------------------------------------------------------------------
    // Family access (the read's server-side resolution)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FamilyAccess_ResolvesOwnedFamiliesButNotDeletedOnes()
    {
        await using FamiliesDbContext families = CreateFamiliesContext();

        UserId owner = UserId.New();
        Family family = Family.Create(owner);
        families.Families.Add(family);
        await families.SaveChangesAsync();

        Result<Guid> access = await new GetFamilyHomeAccessQueryHandler(families).Handle(
            new GetFamilyHomeAccessQuery(owner.Value), CancellationToken.None);
        Assert.True(access.IsSuccess);
        Assert.Equal(family.Id.Value, access.Value);

        Result<Guid> outsider = await new GetFamilyHomeAccessQueryHandler(families).Handle(
            new GetFamilyHomeAccessQuery(Guid.NewGuid()), CancellationToken.None);
        Assert.True(outsider.IsFailure);
        Assert.Equal("Families.Family.NotFound", outsider.Error.Code);

        Result<Guid> anonymous = await new GetFamilyHomeAccessQueryHandler(families).Handle(
            new GetFamilyHomeAccessQuery(Guid.Empty), CancellationToken.None);
        Assert.True(anonymous.IsFailure);
        Assert.Equal("Families.Family.NotFound", anonymous.Error.Code);

        family.MarkDeleted("test", "deleted in test");
        await families.SaveChangesAsync();

        Result<Guid> deleted = await new GetFamilyHomeAccessQueryHandler(families).Handle(
            new GetFamilyHomeAccessQuery(owner.Value), CancellationToken.None);
        Assert.True(deleted.IsFailure);
        Assert.Equal("Families.Family.NotFound", deleted.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static FamilyHomeItemRequest Banner(
        string arabicTitle,
        string englishTitle,
        string imageKey,
        FamilyHomeTargetRequest? target = null) =>
        new(
            ArabicTitle: arabicTitle,
            EnglishTitle: englishTitle,
            ImageKey: imageKey,
            Target: target);

    private static FamilyHomeItemRequest QuickLink(
        string arabicTitle,
        string englishTitle,
        FamilyHomeTargetRequest target,
        string? iconKey = null) =>
        new(
            ArabicTitle: arabicTitle,
            EnglishTitle: englishTitle,
            IconKey: iconKey,
            Target: target);

    private static Task<Result<FamilyHomeAdminSectionResponse>> CreateAsync(
        CmsDbContext db,
        FamilyHomeSectionKind kind,
        IReadOnlyList<FamilyHomeItemRequest>? items,
        int displayOrder = 0) =>
        new CreateFamilyHomeSectionCommandHandler(db).Handle(
            new CreateFamilyHomeSectionCommand(kind, items, displayOrder, IsVisible: true),
            CancellationToken.None);

    private static Task<Result<FamilyHomeAdminSectionResponse>> UpdateAsync(
        CmsDbContext db,
        Guid id,
        IReadOnlyList<FamilyHomeItemRequest>? items,
        int displayOrder,
        int expectedVersion) =>
        new UpdateFamilyHomeSectionCommandHandler(db).Handle(
            new UpdateFamilyHomeSectionCommand(id, items, displayOrder, IsVisible: true, expectedVersion),
            CancellationToken.None);

    private static Task<Result<FamilyHomeAdminSectionResponse>> PublishAsync(
        CmsDbContext db,
        CaregiversDbContext caregivers,
        Guid id) =>
        new PublishFamilyHomeSectionCommandHandler(db, caregivers).Handle(
            new PublishFamilyHomeSectionCommand(id, ActorId, ExpectedVersion: 0),
            CancellationToken.None);

    private static Task<Result> DeleteAsync(CmsDbContext db, Guid id) =>
        new DeleteFamilyHomeSectionCommandHandler(db).Handle(
            new DeleteFamilyHomeSectionCommand(id, ActorId),
            CancellationToken.None);

    private static Task<Result> ReorderAsync(CmsDbContext db, IReadOnlyList<FamilyHomeReorderItem> items) =>
        new ReorderFamilyHomeSectionsCommandHandler(db).Handle(
            new ReorderFamilyHomeSectionsCommand(items),
            CancellationToken.None);

    private static Task<Result<FamilyHomeAdminPageResponse>> ListAsync(
        CmsDbContext db,
        int page,
        int pageSize) =>
        new GetFamilyHomeAdminListQueryHandler(db).Handle(
            new GetFamilyHomeAdminListQuery(Page: page, PageSize: pageSize),
            CancellationToken.None);

    private static async Task<FamilyHomeSection> GetAsync(CmsDbContext db, Guid id) =>
        await db.FamilyHomeSections.SingleAsync(row => row.Id == new FamilyHomeSectionId(id));

    /// <summary>
    /// Publishes a stored row directly, the way the local adapter also inserts a
    /// synthetic published row: publication validation is a separate concern
    /// from the read rules proved on top of such a row.
    /// </summary>
    private static async Task PublishDirectlyAsync(CmsDbContext db, FamilyHomeSection section)
    {
        section.Publish(section.Version);
        db.FamilyHomeSections.Add(section);
        await db.SaveChangesAsync();
    }

    private static async Task<List<FamilyHomeSectionPublicItem>> ReadAsync(
        CmsDbContext db,
        CaregiversDbContext caregivers)
    {
        Result<IReadOnlyList<FamilyHomeSectionPublicItem>> result =
            await new GetPublishedFamilyHomeSectionsQueryHandler(db, caregivers).Handle(
                new GetPublishedFamilyHomeSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value.ToList();
    }

    private static Service SeedService(
        CaregiversDbContext db,
        string arabicName,
        string englishName,
        CaregiverType caregiverType,
        bool isActive = true)
    {
        Service service = Service.Create(
            arabicName, englishName, "icons/svc.png", caregiverType, isActive);
        db.Services.Add(service);
        db.SaveChanges();
        return service;
    }

    private static CmsDbContext CreateCmsContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static CaregiversDbContext CreateCaregiversContext()
    {
        var options = new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CaregiversDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static FamiliesDbContext CreateFamiliesContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new FamiliesDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<CmsDbContext> CreateSqliteCmsContextAsync()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task<CaregiversDbContext> CreateSqliteCaregiversContextAsync()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var db = new CaregiversDbContext(
            new DbContextOptionsBuilder<CaregiversDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
