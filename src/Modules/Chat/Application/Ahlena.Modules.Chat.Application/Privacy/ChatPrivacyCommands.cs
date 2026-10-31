using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Application.Realtime;
using Ahlena.Modules.Chat.Domain.Privacy;

namespace Ahlena.Modules.Chat.Application.Privacy;

public sealed record GetChatPrivacyQuery(UserId Owner)
    : IQuery<ChatPrivacyResponse>;

public sealed class GetChatPrivacyQueryHandler
    : IQueryHandler<GetChatPrivacyQuery, ChatPrivacyResponse>
{
    private readonly IChatDbContext _dbContext;

    public GetChatPrivacyQueryHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ChatPrivacyResponse>> Handle(
        GetChatPrivacyQuery request,
        CancellationToken cancellationToken)
    {
        ChatPrivacySettings? settings = await _dbContext.PrivacySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OwnerUserId == request.Owner,
                cancellationToken);

        // Absence of a row means undisclosed: both flags default false for
        // new and legacy users.
        return Result<ChatPrivacyResponse>.Success(
            settings is null
                ? new ChatPrivacyResponse(false, false)
                : new ChatPrivacyResponse(
                    settings.ShowOnlineStatus,
                    settings.ShareLastSeen));
    }
}

public sealed record UpdateChatPrivacyCommand(
    UserId Owner,
    bool? ShowOnlineStatus,
    bool? ShareLastSeen)
    : ICommand<ChatPrivacyResponse>;

public sealed class UpdateChatPrivacyCommandHandler
    : ICommandHandler<UpdateChatPrivacyCommand, ChatPrivacyResponse>
{
    private readonly IChatDbContext _dbContext;
    private readonly IChatAuthorizationChangedNotifier? _notifier;

    public UpdateChatPrivacyCommandHandler(
        IChatDbContext dbContext,
        IChatAuthorizationChangedNotifier? notifier = null)
    {
        _dbContext = dbContext;
        _notifier = notifier;
    }

    public async Task<Result<ChatPrivacyResponse>> Handle(
        UpdateChatPrivacyCommand request,
        CancellationToken cancellationToken)
    {
        // Full replacement: both booleans are required, never defaulted
        // from omitted JSON fields.
        if (request.ShowOnlineStatus is not { } showOnlineStatus ||
            request.ShareLastSeen is not { } shareLastSeen)
        {
            return Result<ChatPrivacyResponse>.Failure(
                ChatPrivacyErrors.Invalid);
        }

        DateTime now = DateTime.UtcNow;

        ChatPrivacySettings? settings = await _dbContext.PrivacySettings
            .SingleOrDefaultAsync(
                item => item.OwnerUserId == request.Owner,
                cancellationToken);

        if (settings is null)
        {
            settings = ChatPrivacySettings.Create(
                request.Owner,
                showOnlineStatus,
                shareLastSeen,
                now);
            _dbContext.PrivacySettings.Add(settings);
        }
        else
        {
            settings.Replace(showOnlineStatus, shareLastSeen, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Disabling privacy withdraws previously shown state: evict
        // ephemeral typing and let the next snapshot/dispatch re-resolve
        // from these durable settings (documented client reset).
        if (_notifier is not null)
        {
            await _notifier.PresenceSettingsChangedAsync(
                request.Owner,
                cancellationToken);
        }

        return Result<ChatPrivacyResponse>.Success(
            new ChatPrivacyResponse(showOnlineStatus, shareLastSeen));
    }
}
