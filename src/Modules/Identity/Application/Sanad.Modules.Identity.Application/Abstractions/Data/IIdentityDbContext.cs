using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Domain;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Domain.Users;
using FeedbackEntity = Sanad.Modules.Identity.Domain.Feedback;

namespace Sanad.Modules.Identity.Application.Abstractions.Data;

public interface IIdentityDbContext
{
    DbSet<User> Users { get; }

    DbSet<VerificationRequest> VerificationRequests { get; }

    DbSet<DeviceSession> DeviceSessions { get; }

    DbSet<SupportTicket> SupportTickets { get; }

    DbSet<FeedbackEntity> Feedbacks { get; }

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
