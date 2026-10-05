namespace Sanad.Modules.Community.Application.Comments;

public sealed record UpdateCommentCommand : ICommand<Comment?>
{
    public Guid CommentId { get; init; }
    public Guid AuthorId { get; init; }
    public string ContentArabic { get; init; } = string.Empty;
    public string ContentEnglish { get; init; } = string.Empty;
}
