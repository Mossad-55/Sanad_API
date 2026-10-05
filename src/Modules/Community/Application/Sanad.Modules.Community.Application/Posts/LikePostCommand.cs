namespace Sanad.Modules.Community.Application.Posts;

public sealed record LikePostCommand : ICommand<bool>
{
    public Guid PostId { get; init; }
    public Guid UserId { get; init; }
}

public sealed record UnlikePostCommand(Guid PostId, Guid UserId) : ICommand<bool>;
