using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class LandingRefsTests
{
    [Fact]
    public async Task Testimonials_ResolveBothKinds_SkipDanglingAndHidden()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext familiesDb = CreateFamiliesContext();
        await using CaregiversDbContext caregiversDb = CreateCaregiversContext();

        Family family = Family.Create(UserId.New(), "Al-Mansour Family");
        familiesDb.Families.Add(family);

        Booking booking = Booking.Create(
            family.Id,
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Companion,
            BookingShiftType.Hourly,
            new DateOnly(2026, 12, 1),
            new TimeOnly(9, 0),
            new TimeOnly(13, 0),
            "Cairo",
            null,
            BookingPriceSnapshot.Calculate(100m, 0m, 0m, 1),
            UtcNow().AddDays(1),
            DateOnly.FromDateTime(UtcNow()),
            UtcNow());
        familiesDb.Bookings.Add(booking);

        CaregiverRating rating = CaregiverRating.Create(
            CaregiverId.New(), Guid.NewGuid(), family.Id, UserId.New(), 5, "Excellent care", UtcNow());
        caregiversDb.CaregiverRatings.Add(rating);

        BookingReview review = BookingReview.Create(
            booking.Id, UserId.New(), 4, "Good service", false, UtcNow());
        BookingReview anonymous = BookingReview.Create(
            BookingId.New(), UserId.New(), 5, "Lovely", true, UtcNow());
        BookingReview orphan = BookingReview.Create(
            BookingId.New(), UserId.New(), 3, "Okay", false, UtcNow());
        familiesDb.BookingReviews.AddRange(review, anonymous, orphan);
        await familiesDb.SaveChangesAsync();
        await caregiversDb.SaveChangesAsync();

        Guid dangling = Guid.NewGuid();
        string json = "{\"refs\":[" +
            "{\"kind\":\"family\",\"id\":\"" + review.Id.Value + "\"}," +
            "{\"kind\":\"caregiver\",\"id\":\"" + rating.Id + "\"}," +
            "{\"kind\":\"family\",\"id\":\"" + anonymous.Id.Value + "\",\"hidden\":true}," +
            "{\"kind\":\"family\",\"id\":\"" + orphan.Id.Value + "\"}," +
            "{\"kind\":\"family\",\"id\":\"" + dangling + "\"}," +
            "{\"kind\":\"mystery\",\"id\":\"" + dangling + "\"}," +
            "{\"kind\":\"caregiver\",\"id\":\"" + rating.Id + "\",\"hidden\":true}]}";

        LandingSection section = LandingSection.Create(
            LandingSectionType.Testimonials, "آراء", "Testimonials", contentJson: json);
        section.Publish();
        db.LandingSections.Add(section);
        await db.SaveChangesAsync();

        var result = await new GetPublishedLandingSectionsQueryHandler(db, familiesDb, caregiversDb).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        LandingSectionPublicItem single = Assert.Single(result.Value);
        Assert.NotNull(single.Testimonials);
        Assert.Equal(3, single.Testimonials.Count);
        Assert.Equal("family", single.Testimonials[0].Kind);
        Assert.Equal(4, single.Testimonials[0].Stars);
        Assert.Equal("Good service", single.Testimonials[0].Text);
        Assert.Equal("Al-Mansour Family", single.Testimonials[0].ArabicName);
        Assert.Equal("caregiver", single.Testimonials[1].Kind);
        Assert.Equal(5, single.Testimonials[1].Stars);
        Assert.Equal("Al-Mansour Family", single.Testimonials[1].EnglishName);
        Assert.Equal("family", single.Testimonials[2].Kind);
        Assert.Equal("Verified family", single.Testimonials[2].EnglishName);

        var admin = await new GetLandingAdminListQueryHandler(db, familiesDb, caregiversDb).Handle(
            new GetLandingAdminListQuery(), CancellationToken.None);
        Assert.True(admin.IsSuccess);
        Assert.Equal(2, admin.Value.Items.Single().DanglingRefCount);
    }

    [Fact]
    public async Task Testimonials_PreserveGenuineRatingsAndAnonymity_WithoutInventingIdentity()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext familiesDb = CreateFamiliesContext();
        await using CaregiversDbContext caregiversDb = CreateCaregiversContext();

        Family named = Family.Create(UserId.New(), "Named Family");
        familiesDb.Families.Add(named);

        Booking booking = Booking.Create(
            named.Id,
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Companion,
            BookingShiftType.Hourly,
            new DateOnly(2026, 12, 1),
            new TimeOnly(9, 0),
            new TimeOnly(13, 0),
            "Cairo",
            null,
            BookingPriceSnapshot.Calculate(100m, 0m, 0m, 1),
            UtcNow().AddDays(1),
            DateOnly.FromDateTime(UtcNow()),
            UtcNow());
        familiesDb.Bookings.Add(booking);

        BookingReview attributed = BookingReview.Create(
            booking.Id, UserId.New(), 5, "Genuine words", false, UtcNow());
        BookingReview anonymous = BookingReview.Create(
            BookingId.New(), UserId.New(), 3, "Private words", true, UtcNow());
        familiesDb.BookingReviews.AddRange(attributed, anonymous);

        CaregiverRating rating = CaregiverRating.Create(
            CaregiverId.New(), Guid.NewGuid(), named.Id, UserId.New(), 4, "Caregiver words", UtcNow());
        caregiversDb.CaregiverRatings.Add(rating);

        await familiesDb.SaveChangesAsync();
        await caregiversDb.SaveChangesAsync();

        string json = "{\"refs\":[" +
            "{\"kind\":\"family\",\"id\":\"" + attributed.Id.Value + "\"}," +
            "{\"kind\":\"family\",\"id\":\"" + anonymous.Id.Value + "\"}," +
            "{\"kind\":\"caregiver\",\"id\":\"" + rating.Id + "\"}]}";

        LandingSection section = LandingSection.Create(
            LandingSectionType.Testimonials, "آراء", "Voices", contentJson: json);
        section.Publish();
        db.LandingSections.Add(section);
        await db.SaveChangesAsync();

        var result = await new GetPublishedLandingSectionsQueryHandler(db, familiesDb, caregiversDb).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        TestimonialItem[] items = Assert.Single(result.Value).Testimonials!.ToArray();
        Assert.Equal(3, items.Length);

        // Genuine ratings and the authored text are preserved verbatim.
        Assert.Equal(new[] { 5, 3, 4 }, items.Select(item => item.Stars).ToArray());
        Assert.Equal(
            new[] { "Genuine words", "Private words", "Caregiver words" },
            items.Select(item => item.Text).ToArray());

        // The attributed family keeps its real name; the anonymous one is served
        // the verified-family badge and never its name.
        Assert.Equal("Named Family", items[0].EnglishName);
        Assert.Equal("Verified family", items[1].EnglishName);
        Assert.Equal("عائلة موثقة", items[1].ArabicName);

        // The wire shape carries only the six real testimonial members — no
        // invented partner role, endorsement or extra identity field.
        // Serialized with the API's web defaults (camelCase) so this asserts the
        // real wire shape.
        string wire = JsonSerializer.Serialize(
            items[1],
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("Named Family", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("anonymous", wire, StringComparison.OrdinalIgnoreCase);

        using JsonDocument document = JsonDocument.Parse(wire);
        Assert.Equal(
            new[] { "arabicName", "englishName", "kind", "refId", "stars", "text" },
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task Faq_ResolvesActive_DropsInactiveAndMissing_InRefOrder()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext familiesDb = CreateFamiliesContext();
        await using CaregiversDbContext caregiversDb = CreateCaregiversContext();

        HelpFaq second = HelpFaq.Create(
            LegalAudience.Family, "س2", "Q2", "ج2", "A2", displayOrder: 0, isActive: true);
        HelpFaq first = HelpFaq.Create(
            LegalAudience.Family, "س1", "Q1", "ج1", "A1", displayOrder: 5, isActive: true);
        HelpFaq retired = HelpFaq.Create(
            LegalAudience.Family, "س3", "Q3", "ج3", "A3", displayOrder: 0, isActive: false);
        db.HelpFaqs.AddRange(second, first, retired);
        await db.SaveChangesAsync();

        string json = "{\"refs\":[" +
            "{\"id\":\"" + first.Id.Value + "\"}," +
            "{\"id\":\"" + retired.Id.Value + "\"}," +
            "{\"id\":\"" + Guid.NewGuid() + "\"}," +
            "{\"id\":\"" + second.Id.Value + "\",\"hidden\":true}]}";

        LandingSection section = LandingSection.Create(
            LandingSectionType.Faq, "أسئلة", "FAQ", contentJson: json);
        section.Publish();
        db.LandingSections.Add(section);
        await db.SaveChangesAsync();

        var result = await new GetPublishedLandingSectionsQueryHandler(db, familiesDb, caregiversDb).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        LandingSectionPublicItem single = Assert.Single(result.Value);
        Assert.NotNull(single.Faqs);
        LandingFaqItem only = Assert.Single(single.Faqs);
        Assert.Equal("Q1", only.EnglishQuestion);
        Assert.Equal("A1", only.EnglishAnswer);

        var admin = await new GetLandingAdminListQueryHandler(db, familiesDb, caregiversDb).Handle(
            new GetLandingAdminListQuery(), CancellationToken.None);
        Assert.True(admin.IsSuccess);
        Assert.Equal(2, admin.Value.Items.Single().DanglingRefCount);
    }

    [Fact]
    public async Task MalformedContentJson_Never500()
    {
        await using CmsDbContext db = CreateCmsContext();
        await using FamiliesDbContext familiesDb = CreateFamiliesContext();
        await using CaregiversDbContext caregiversDb = CreateCaregiversContext();

        LandingSection broken = LandingSection.Create(
            LandingSectionType.Testimonials, "آراء", "Broken", contentJson: "{not json");
        broken.Publish();
        LandingSection wrongShape = LandingSection.Create(
            LandingSectionType.Faq, "أسئلة", "Wrong", contentJson: "{\"refs\":\"nope\"}");
        wrongShape.Publish();
        db.LandingSections.AddRange(broken, wrongShape);
        await db.SaveChangesAsync();

        var result = await new GetPublishedLandingSectionsQueryHandler(db, familiesDb, caregiversDb).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Empty(result.Value[0].Testimonials ?? []);
        Assert.Empty(result.Value[1].Faqs ?? []);
    }

    [Fact]
    public async Task Refs_TranslateOnRelationalStore()
    {
        using var cmsConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await cmsConnection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(cmsConnection).Options);
        await db.Database.EnsureCreatedAsync();

        using var familiesConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await familiesConnection.OpenAsync();
        await using var familiesDb = new FamiliesDbContext(
            new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(familiesConnection).Options);
        await familiesDb.Database.EnsureCreatedAsync();

        using var caregiversConnection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await caregiversConnection.OpenAsync();
        await using var caregiversDb = new CaregiversDbContext(
            new DbContextOptionsBuilder<CaregiversDbContext>().UseSqlite(caregiversConnection).Options);
        await caregiversDb.Database.EnsureCreatedAsync();

        Family family = Family.Create(UserId.New(), "Relational Family");
        familiesDb.Families.Add(family);
        await familiesDb.SaveChangesAsync();

        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        caregiversDb.Caregivers.Add(caregiver);
        await caregiversDb.SaveChangesAsync();

        CaregiverRating rating = CaregiverRating.Create(
            caregiver.Id, Guid.NewGuid(), family.Id, UserId.New(), 5, "Relational care", UtcNow());
        caregiversDb.CaregiverRatings.Add(rating);
        await caregiversDb.SaveChangesAsync();

        HelpFaq faq = HelpFaq.Create(
            LegalAudience.Family, "س", "Relational Q", "ج", "Relational A", displayOrder: 0, isActive: true);
        db.HelpFaqs.Add(faq);
        await db.SaveChangesAsync();

        LandingSection testimonials = LandingSection.Create(
            LandingSectionType.Testimonials, "آراء", "Voices",
            contentJson: "{\"refs\":[{\"kind\":\"caregiver\",\"id\":\"" + rating.Id + "\"},{\"kind\":\"family\",\"id\":\"" + Guid.NewGuid() + "\"}]}",
            displayOrder: 0);
        testimonials.Publish();
        LandingSection faqSection = LandingSection.Create(
            LandingSectionType.Faq, "أسئلة", "Questions",
            contentJson: "{\"refs\":[{\"id\":\"" + faq.Id.Value + "\"}]}",
            displayOrder: 1);
        faqSection.Publish();
        db.LandingSections.AddRange(testimonials, faqSection);
        await db.SaveChangesAsync();

        var result = await new GetPublishedLandingSectionsQueryHandler(db, familiesDb, caregiversDb).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        TestimonialItem resolved = Assert.Single(result.Value[0].Testimonials ?? []);
        Assert.Equal("Relational Family", resolved.EnglishName);
        LandingFaqItem resolvedFaq = Assert.Single(result.Value[1].Faqs ?? []);
        Assert.Equal("Relational A", resolvedFaq.EnglishAnswer);

        var admin = await new GetLandingAdminListQueryHandler(db, familiesDb, caregiversDb).Handle(
            new GetLandingAdminListQuery(), CancellationToken.None);
        Assert.True(admin.IsSuccess);
        Assert.Equal(1, admin.Value.Items[0].DanglingRefCount);
        Assert.Equal(0, admin.Value.Items[1].DanglingRefCount);
    }

    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    private static CmsDbContext CreateCmsContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
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

    private static CaregiversDbContext CreateCaregiversContext()
    {
        var options = new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CaregiversDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
