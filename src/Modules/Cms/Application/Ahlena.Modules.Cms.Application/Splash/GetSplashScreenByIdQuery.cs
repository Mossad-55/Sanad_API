using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Splash;

namespace Ahlena.Modules.Cms.Application.Splash;

public sealed record GetSplashScreenByIdQuery(
    SplashScreenId Id)
    : IQuery<SplashScreenResponse>;

public sealed class GetSplashScreenByIdQueryValidator
    : AbstractValidator<GetSplashScreenByIdQuery>
{
    public GetSplashScreenByIdQueryValidator()
    {
        RuleFor(q => q.Id).NotEqual(SplashScreenId.Empty);
    }
}

public sealed class GetSplashScreenByIdQueryHandler
    : IQueryHandler<GetSplashScreenByIdQuery, SplashScreenResponse>
{
    private readonly ICmsDbContext _dbContext;

    public GetSplashScreenByIdQueryHandler(ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SplashScreenResponse>> Handle(
        GetSplashScreenByIdQuery request,
        CancellationToken cancellationToken)
    {
        SplashScreen? screen = await _dbContext.SplashScreens
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (screen is null)
        {
            return SplashErrors.NotFound;
        }

        return screen.ToResponse();
    }
}