using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Abstractions.Data;

public interface IChatDbContext
{
    DbSet<Conversation> Conversations { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<Block> Blocks { get; }
    DbSet<ConversationReadState> ReadStates { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
