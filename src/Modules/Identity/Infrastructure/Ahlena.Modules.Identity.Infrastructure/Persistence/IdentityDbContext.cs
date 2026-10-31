using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using FeedbackEntity = Ahlena.Modules.Identity.Domain.Feedback;
using Ahlena.Modules.Identity.Domain.Support;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext :
    DbContext,
    IIdentityDbContext
{
    public const string Schema = "identity";

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users =>
        Set<User>();

    public DbSet<VerificationRequest>
        VerificationRequests =>
            Set<VerificationRequest>();

    public DbSet<DeviceSession>
        DeviceSessions =>
            Set<DeviceSession>();

    public DbSet<SupportTicket>
        SupportTickets =>
            Set<SupportTicket>();

    public DbSet<SupportTicketReply>
        SupportTicketReplies =>
            Set<SupportTicketReply>();

    public DbSet<FeedbackEntity>
        Feedbacks =>
            Set<FeedbackEntity>();

    public DbSet<AdminUserAction> AdminUserActions => Set<AdminUserAction>();

    public DbSet<PartnerContact>
        PartnerContacts =>
            Set<PartnerContact>();


    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_user_accounts_one_caregiver" })
        {
            throw new AccountWriteConflictException(true, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_user_accounts_user_id_account_type" })
        {
            throw new AccountWriteConflictException(false, ex);
        }
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(
            Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(IdentityDbContext).Assembly);

        base.OnModelCreating(
            modelBuilder);
    }
}
