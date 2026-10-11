using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
