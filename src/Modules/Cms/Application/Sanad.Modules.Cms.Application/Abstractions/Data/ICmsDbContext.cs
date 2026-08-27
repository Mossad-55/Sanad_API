using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Abstractions.Data;

public interface ICmsDbContext
{
    DbSet<SplashScreen> SplashScreens { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}