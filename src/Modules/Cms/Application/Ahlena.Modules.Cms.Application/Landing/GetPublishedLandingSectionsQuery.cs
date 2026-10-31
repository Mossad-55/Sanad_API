using Ahlena.BuildingBlocks.Application.CQRS;

namespace Ahlena.Modules.Cms.Application.Landing;

public sealed record GetPublishedLandingSectionsQuery()
    : IQuery<IReadOnlyList<LandingSectionPublicItem>>;
