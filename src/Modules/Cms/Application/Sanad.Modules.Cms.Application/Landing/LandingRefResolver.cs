using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Cms.Application.Landing;

internal sealed record TestimonialRef(string Kind, Guid Id, bool Hidden);

internal sealed record FaqRef(Guid Id, bool Hidden);

internal sealed record ResolvedSectionRefs(
    IReadOnlyList<TestimonialItem> Testimonials,
    IReadOnlyList<LandingFaqItem> Faqs,
    int DanglingRefCount);

/// <summary>
/// Resolves testimonial/FAQ refs stored in section ContentJson. Dangling refs
/// (missing or inactive targets, corrupt entries) resolve as skipped, never
/// throwing — public reads stay 200 with resolved items only.
/// </summary>
internal static class LandingRefResolver
{
    public const string VerifiedFamilyArabicName = "عائلة موثقة";
    public const string VerifiedFamilyEnglishName = "Verified family";

    public static IReadOnlyList<TestimonialRef> ParseTestimonials(string? contentJson)
    {
        var refs = new List<TestimonialRef>();
        foreach (JsonElement entry in SelectRefs(contentJson))
        {
            if (!TryGetGuid(entry, "id", out Guid id))
                continue;

            string kind = TryGetString(entry, "kind")?.Trim().ToLowerInvariant() ?? string.Empty;
            refs.Add(new TestimonialRef(kind, id, IsHidden(entry)));
        }

        return refs;
    }

    public static IReadOnlyList<FaqRef> ParseFaqs(string? contentJson)
    {
        var refs = new List<FaqRef>();
        foreach (JsonElement entry in SelectRefs(contentJson))
        {
            if (!TryGetGuid(entry, "id", out Guid id))
                continue;

            refs.Add(new FaqRef(id, IsHidden(entry)));
        }

        return refs;
    }

    public static async Task<Dictionary<Guid, ResolvedSectionRefs>> ResolveAsync(
        ICmsDbContext cmsDb,
        IFamiliesDbContext familiesDb,
        ICaregiversDbContext caregiversDb,
        IReadOnlyList<LandingSection> sections,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ResolvedSectionRefs>();

        foreach (LandingSection section in sections)
        {
            if (section.Type == LandingSectionType.Testimonials)
            {
                result[section.Id.Value] = await ResolveTestimonialsAsync(
                    familiesDb, caregiversDb, ParseTestimonials(section.ContentJson), cancellationToken);
            }
            else if (section.Type == LandingSectionType.Faq)
            {
                result[section.Id.Value] = await ResolveFaqsAsync(
                    cmsDb, ParseFaqs(section.ContentJson), cancellationToken);
            }
        }

        return result;
    }

    private static async Task<ResolvedSectionRefs> ResolveTestimonialsAsync(
        IFamiliesDbContext familiesDb,
        ICaregiversDbContext caregiversDb,
        IReadOnlyList<TestimonialRef> refs,
        CancellationToken cancellationToken)
    {
        var items = new List<TestimonialItem>();
        int dangling = 0;

        List<Guid> caregiverIds = refs
            .Where(entry => !entry.Hidden && entry.Kind == "caregiver")
            .Select(entry => entry.Id)
            .ToList();

        Dictionary<Guid, CaregiverRating> ratings = [];
        if (caregiverIds.Count > 0)
        {
            ratings = await caregiversDb.CaregiverRatings
                .AsNoTracking()
                .Where(rating => caregiverIds.Contains(rating.Id))
                .ToDictionaryAsync(rating => rating.Id, cancellationToken);
        }

        foreach (TestimonialRef entry in refs)
        {
            if (entry.Hidden)
                continue;

            if (entry.Kind == "caregiver")
            {
                if (!ratings.TryGetValue(entry.Id, out CaregiverRating? rating))
                {
                    dangling++;
                    continue;
                }

                (string arabicName, string englishName) = await FamilyNamesOrFallbackAsync(
                    familiesDb, rating.FamilyId, cancellationToken);
                items.Add(new TestimonialItem(
                    "caregiver",
                    entry.Id,
                    rating.Stars,
                    string.IsNullOrWhiteSpace(rating.ReviewText) ? null : rating.ReviewText,
                    arabicName,
                    englishName));
            }
            else if (entry.Kind == "family")
            {
                BookingReview? review = await familiesDb.BookingReviews
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.Id == new BookingReviewId(entry.Id), cancellationToken);
                if (review is null)
                {
                    dangling++;
                    continue;
                }

                string arabicName = VerifiedFamilyArabicName;
                string englishName = VerifiedFamilyEnglishName;
                if (!review.IsAnonymous)
                {
                    (arabicName, englishName) = await ReviewFamilyNamesAsync(
                        familiesDb, review, cancellationToken);
                }

                items.Add(new TestimonialItem(
                    "family",
                    entry.Id,
                    review.Rating,
                    string.IsNullOrWhiteSpace(review.Comment) ? null : review.Comment,
                    arabicName,
                    englishName));
            }
            else
            {
                dangling++;
            }
        }

        return new ResolvedSectionRefs(items, [], dangling);
    }

    private static async Task<ResolvedSectionRefs> ResolveFaqsAsync(
        ICmsDbContext cmsDb,
        IReadOnlyList<FaqRef> refs,
        CancellationToken cancellationToken)
    {
        var items = new List<LandingFaqItem>();
        int dangling = 0;

        foreach (FaqRef entry in refs)
        {
            if (entry.Hidden)
                continue;

            HelpFaq? faq = await cmsDb.HelpFaqs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == new HelpFaqId(entry.Id), cancellationToken);
            if (faq is null || !faq.IsActive)
            {
                dangling++;
                continue;
            }

            items.Add(new LandingFaqItem(
                entry.Id,
                faq.ArabicQuestion,
                faq.EnglishQuestion,
                faq.ArabicAnswer,
                faq.EnglishAnswer));
        }

        return new ResolvedSectionRefs([], items, dangling);
    }

    private static async Task<(string Arabic, string English)> FamilyNamesOrFallbackAsync(
        IFamiliesDbContext familiesDb, FamilyId familyId, CancellationToken cancellationToken)
    {
        Family? family = await familiesDb.Families
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == familyId, cancellationToken);
        if (family is null || string.IsNullOrWhiteSpace(family.Name))
            return (VerifiedFamilyArabicName, VerifiedFamilyEnglishName);

        return (family.Name, family.Name);
    }

    private static async Task<(string Arabic, string English)> ReviewFamilyNamesAsync(
        IFamiliesDbContext familiesDb, BookingReview review, CancellationToken cancellationToken)
    {
        Booking? booking = await familiesDb.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == review.BookingId, cancellationToken);
        if (booking is null)
            return (VerifiedFamilyArabicName, VerifiedFamilyEnglishName);

        return await FamilyNamesOrFallbackAsync(familiesDb, booking.FamilyId, cancellationToken);
    }

    private static bool TryGetGuid(JsonElement entry, string name, out Guid value)
    {
        value = Guid.Empty;
        return entry.ValueKind == JsonValueKind.Object &&
            entry.TryGetProperty(name, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            Guid.TryParse(property.GetString(), out value) &&
            value != Guid.Empty;
    }

    private static string? TryGetString(JsonElement entry, string name)
    {
        if (entry.ValueKind != JsonValueKind.Object ||
            !entry.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        return property.GetString();
    }

    private static bool IsHidden(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object &&
        entry.TryGetProperty("hidden", out JsonElement property) &&
        property.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<JsonElement> SelectRefs(string? contentJson)
    {
        var refs = new List<JsonElement>();
        if (string.IsNullOrWhiteSpace(contentJson))
            return refs;

        try
        {
            using JsonDocument document = JsonDocument.Parse(contentJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("refs", out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
                return refs;

            foreach (JsonElement entry in array.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object)
                    refs.Add(entry.Clone());
            }
        }
        catch (JsonException)
        {
            refs.Clear();
        }

        return refs;
    }
}
