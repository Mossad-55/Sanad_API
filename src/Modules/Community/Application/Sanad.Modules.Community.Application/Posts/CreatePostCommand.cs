namespace Sanad.Modules.Community.Application.Posts;

public sealed record CreatePostCommand : ICommand<Post>
{
    public string TitleArabic { get; init; } = string.Empty;
    public string TitleEnglish { get; init; } = string.Empty;
    public string ContentArabic { get; init; } = string.Empty;
    public string ContentEnglish { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public bool IsAnonymous { get; init; }
    public Guid AuthorId { get; init; }
}
