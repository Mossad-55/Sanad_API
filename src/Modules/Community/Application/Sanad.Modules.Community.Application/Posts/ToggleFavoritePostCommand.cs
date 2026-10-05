namespace Sanad.Modules.Community.Application.Posts;

public sealed record ToggleFavoritePostCommand(Guid PostId, Guid UserId) : ICommand<bool>
{
}
