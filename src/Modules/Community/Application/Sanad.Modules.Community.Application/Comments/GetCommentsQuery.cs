namespace Sanad.Modules.Community.Application.Comments;

public sealed record GetCommentsQuery(Guid PostId, int Page, int PageSize) : IQuery<Comment[]>;
