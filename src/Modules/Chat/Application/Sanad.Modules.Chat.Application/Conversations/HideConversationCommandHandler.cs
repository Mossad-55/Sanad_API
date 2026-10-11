using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Conversations;

public sealed class HideConversationCommandHandler
    : ICommandHandler<HideConversationCommand>
{
    private readonly IChatDbContext _dbContext;

    public HideConversationCommandHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        HideConversationCommand request,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        // Unknown and foreign behave identically, so a stranger cannot use
        // this endpoint to probe which conversation ids exist.
        if (conversation is null ||
            !conversation.IsParticipant(request.Requester))
        {
            return Result.Failure(ConversationErrors.NotFound);
        }

        if (!conversation.IsHiddenFor(request.Requester))
        {
            conversation.Hide(request.Requester);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Already hidden (or hidden again) still succeeds — 204 idempotent.
        return Result.Success();
    }
}
