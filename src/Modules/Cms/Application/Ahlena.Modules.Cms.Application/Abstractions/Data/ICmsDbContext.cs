using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Cms.Domain.Help;
using Ahlena.Modules.Cms.Domain.Landing;
using Ahlena.Modules.Cms.Domain.Legal;
using Ahlena.Modules.Cms.Domain.Library;
using Ahlena.Modules.Cms.Domain.Splash;
using Ahlena.Modules.Cms.Domain.Wellness;
using Ahlena.Modules.Cms.Domain.SentenceBuilder;
using Ahlena.Modules.Cms.Domain.MedicationLateness;
using Ahlena.Modules.Cms.Domain.Settings;
using Ahlena.Modules.Cms.Domain.Welcome;

namespace Ahlena.Modules.Cms.Application.Abstractions.Data;

public interface ICmsDbContext
{
    DbSet<SplashScreen> SplashScreens { get; }

    DbSet<LegalDocument> LegalDocuments { get; }

    DbSet<HelpFaq> HelpFaqs { get; }

    DbSet<SupportContact> SupportContacts { get; }

    DbSet<WellnessTip> WellnessTips { get; }
    DbSet<SentenceBuilderCatalogEntry> SentenceBuilderCatalogEntries { get; }
    DbSet<SentenceBuilderCatalogRevision> SentenceBuilderCatalogRevisions { get; }
    DbSet<MedicationLatenessSetting> MedicationLatenessSettings { get; }
    DbSet<MedicationLatenessSettingRevision> MedicationLatenessSettingRevisions { get; }
    DbSet<ElderlyWelcome> ElderlyWelcomes { get; }

    DbSet<LibraryCategory> LibraryCategories { get; }

    DbSet<LibraryContent> LibraryContents { get; }

    DbSet<LibraryContentInteraction> LibraryContentInteractions { get; }

    DbSet<LandingSection> LandingSections { get; }

    DbSet<LandingSectionHistory> LandingSectionHistory { get; }

    DbSet<FamilyHomeSection> FamilyHomeSections { get; }

    DbSet<FamilyHomeSectionHistory> FamilyHomeSectionHistory { get; }

    DbSet<SiteSettings> SiteSettings { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
