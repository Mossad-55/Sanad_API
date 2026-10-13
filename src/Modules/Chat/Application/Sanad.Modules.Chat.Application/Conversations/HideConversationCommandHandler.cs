using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Conversations;

public sealed class HideConversationCommandHandler
    : ICommandHandler<HideConversationCommand>
{
    private readonly IChatDbContext _dbContext;
    private readonly IChatAuthorizationChangedNotifier? _notifier;

    public HideConversationCommandHandler(
        IChatDbContext dbContext,
        IChatAuthorizationChangedNotifier? notifier = null)
    {
        _dbContext = dbContext;
        _notifier = notifier;
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

            // Hidden conversations contribute no realtime events for the
            // hider; evict their joins before further dispatch.
            if (_notifier is not null)
            {
                await _notifier.ConversationHiddenAsync(
                    request.ConversationId,
                    request.Requester,
                    cancellationToken);
            }
        }

        // Already hidden (or hidden again) still succeeds — 204 idempotent.
        return Result.Success();
    }
}
