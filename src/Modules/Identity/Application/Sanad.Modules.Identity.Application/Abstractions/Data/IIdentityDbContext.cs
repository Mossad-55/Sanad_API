using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Abstractions.Data;

public interface IIdentityDbContext
{
    DbSet<User> Users { get; }

    DbSet<VerificationRequest> VerificationRequests { get; }

    DbSet<DeviceSession> DeviceSessions { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}