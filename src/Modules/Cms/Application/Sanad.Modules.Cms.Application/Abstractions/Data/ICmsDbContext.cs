using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Abstractions.Data;

public interface ICmsDbContext
{
    DbSet<SplashScreen> SplashScreens { get; }

    DbSet<LegalDocument> LegalDocuments { get; }

    DbSet<HelpFaq> HelpFaqs { get; }

    DbSet<SupportContact> SupportContacts { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
