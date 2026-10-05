namespace Sanad.Modules.Community.Application.Comments;

public sealed record DeleteCommentCommand(Guid CommentId, Guid AuthorId) : ICommand<CommentDeleteResult>;
