using Sanad.BuildingBlocks.Application.CQRS;

namespace Sanad.Modules.Cms.Application.Landing;

public sealed record GetPublishedLandingSectionsQuery()
    : IQuery<IReadOnlyList<LandingSectionPublicItem>>;
