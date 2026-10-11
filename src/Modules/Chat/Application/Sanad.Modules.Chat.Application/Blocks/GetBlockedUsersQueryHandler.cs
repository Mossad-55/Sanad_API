using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Blocks;

namespace Sanad.Modules.Chat.Application.Blocks;

public sealed class GetBlockedUsersQueryHandler
    : IQueryHandler<GetBlockedUsersQuery, Guid[]>
{
    private readonly IChatDbContext _dbContext;

    public GetBlockedUsersQueryHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Guid[]>> Handle(
        GetBlockedUsersQuery request,
        CancellationToken cancellationToken)
    {
        List<Block> blocks = await _dbContext.Blocks
            .AsNoTracking()
            // Per-user isolation: only rows the requester created. Ordering
            // is defined as most-recent block first so clients can render a
            // stable, meaningful list.
            .Where(block => block.BlockerUserId == request.Requester)
            .OrderByDescending(block => block.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        var blockedUserIds = new Guid[blocks.Count];

        for (int index = 0; index < blocks.Count; index++)
        {
            blockedUserIds[index] = blocks[index].BlockedUserId.Value;
        }

        return Result<Guid[]>.Success(blockedUserIds);
    }
}
