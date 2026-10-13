using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Changes;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Outbox;
using Sanad.Modules.Chat.Domain.Privacy;
using Sanad.Modules.Chat.Domain.Reports;

namespace Sanad.Modules.Chat.Application.Abstractions.Data;

public interface IChatDbContext
{
    DbSet<Conversation> Conversations { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<Block> Blocks { get; }
    DbSet<ConversationReadState> ReadStates { get; }

    /// <summary>Lane B: immutable messages (endpoints 3, 4, 8 read/write).</summary>
    DbSet<Message> Messages { get; }

    /// <summary>Lane B: user reports (endpoint 11 submit, support queue 12-15).</summary>
    DbSet<Report> Reports { get; }

    /// <summary>Chat Plus: durable per-conversation change log (sync).</summary>
    DbSet<ChatChange> Changes { get; }

    /// <summary>Chat Plus: durable notification intent (dispatcher).</summary>
    DbSet<ChatOutboxItem> Outbox { get; }

    /// <summary>Chat Plus: durable per-user presence privacy settings.</summary>
    DbSet<ChatPrivacySettings> PrivacySettings { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
