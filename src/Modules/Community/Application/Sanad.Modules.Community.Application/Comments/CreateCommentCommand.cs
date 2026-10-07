namespace Sanad.Modules.Community.Application.Comments;

public sealed record CreateCommentCommand : ICommand<Comment>
{
    public Guid PostId { get; init; }
    public string ContentArabic { get; init; } = string.Empty;
    public string ContentEnglish { get; init; } = string.Empty;
    public Guid AuthorId { get; init; }
}
