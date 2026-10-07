using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Comments;

public sealed class GetCommentsQueryHandler : IQueryHandler<GetCommentsQuery, Comment[]>
{
    private readonly ICommunityDbContext _dbContext;

    public GetCommentsQueryHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Comment[]>> Handle(GetCommentsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        Comment[] comments = await _dbContext.Comments
            .AsNoTracking()
            .Where(c => c.PostId == new CommunityPostId(request.PostId))
            .OrderByDescending(c => c.CreatedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return Result<Comment[]>.Success(comments);
    }
}
