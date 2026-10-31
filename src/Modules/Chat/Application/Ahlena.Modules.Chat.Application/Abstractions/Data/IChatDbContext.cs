using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Chat.Domain.Attachments;
using Ahlena.Modules.Chat.Domain.Blocks;
using Ahlena.Modules.Chat.Domain.Changes;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Domain.Outbox;
using Ahlena.Modules.Chat.Domain.Privacy;
using Ahlena.Modules.Chat.Domain.Reports;

namespace Ahlena.Modules.Chat.Application.Abstractions.Data;

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
