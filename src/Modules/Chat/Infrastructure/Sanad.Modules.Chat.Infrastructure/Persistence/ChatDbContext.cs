using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Changes;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Outbox;
using Sanad.Modules.Chat.Domain.Privacy;
using Sanad.Modules.Chat.Domain.Reports;

namespace Sanad.Modules.Chat.Infrastructure.Persistence;

public class ChatDbContext : DbContext, IChatDbContext
{
    public const string Schema = "chat";

    public ChatDbContext(DbContextOptions<ChatDbContext> options)
        : base(options)
    {
    }

    public DbSet<Conversation> Conversations { get; set; } = null!;
    public DbSet<Attachment> Attachments { get; set; } = null!;
    public DbSet<Block> Blocks { get; set; } = null!;
    public DbSet<ConversationReadState> ReadStates { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<Report> Reports { get; set; } = null!;
    public DbSet<ChatChange> Changes { get; set; } = null!;
    public DbSet<ChatOutboxItem> Outbox { get; set; } = null!;
    public DbSet<ChatPrivacySettings> PrivacySettings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly);
    }
}
