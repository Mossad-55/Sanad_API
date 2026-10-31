using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Identity.Domain;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.Modules.Identity.Domain.Support;
using Ahlena.Modules.Identity.Domain.Users;
using FeedbackEntity = Ahlena.Modules.Identity.Domain.Feedback;

namespace Ahlena.Modules.Identity.Application.Abstractions.Data;

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
