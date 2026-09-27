using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Domain.Splash;
using Sanad.Modules.Cms.Domain.Wellness;
using Sanad.Modules.Cms.Domain.SentenceBuilder;
using Sanad.Modules.Cms.Domain.MedicationLateness;

namespace Sanad.Modules.Cms.Application.Abstractions.Data;

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

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
