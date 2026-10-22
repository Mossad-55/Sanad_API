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

    DbSet<SupportTicketReply> SupportTicketReplies => null!;

    DbSet<FeedbackEntity> Feedbacks { get; }

    DbSet<AdminUserAction> AdminUserActions { get; }

    /// <summary>
    /// The anonymous partner-contact rows of the admin support inbox. Like
    /// <see cref="SupportTicketReplies"/>, the default implementation keeps
    /// the delegating test contexts compiling; the real Identity context
    /// provides the set.
    /// </summary>
    DbSet<PartnerContact> PartnerContacts => null!;

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
