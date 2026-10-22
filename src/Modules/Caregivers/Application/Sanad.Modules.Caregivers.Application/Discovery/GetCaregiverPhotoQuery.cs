using FluentValidation;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;

namespace Sanad.Modules.Caregivers.Application.Discovery;

public sealed record CaregiverPhotoFileContent(
    string ContentType,
    Stream Content);

/// <summary>
/// Authorized download of the caregiver's own avatar bytes for any Normal JWT
/// (the S3 approved photo route, refund decision lock #32). The caregiver row
/// is resolved through the SAME discoverability predicate the public search
/// uses — Active status, <c>show_profile</c> not disabled and the identity join
/// — so an unknown, inactive or hidden caregiver is an honest 404 and this
/// route never widens discovery visibility.
/// <para>
/// The response is the image bytes only. The stored Identity
/// <c>avatar_url</c> is a private storage key
/// (<c>private/avatars/…</c>) and is **never** returned on the wire: the route
/// is a download, not a key-to-URL mapping. A missing caregiver, a missing row
/// or a missing/unavailable avatar file all answer the same generic 404 so the
/// route does not distinguish "no avatar" from "not discoverable".
/// </para>
/// </summary>
public sealed record GetCaregiverPhotoQuery(
    CaregiverId CaregiverId) : IQuery<CaregiverPhotoFileContent>;

public sealed class GetCaregiverPhotoQueryValidator : AbstractValidator<GetCaregiverPhotoQuery>
{
    public GetCaregiverPhotoQueryValidator()
    {
        RuleFor(query => query.CaregiverId).NotEqual(CaregiverId.Empty);
    }
}

public static class CaregiverPhotoErrors
{
    public static readonly Error NotFound =
        new(
            "Caregivers.Photo.NotFound",
            "Caregiver photo was not found.");
}

public sealed class GetCaregiverPhotoQueryHandler : IQueryHandler<GetCaregiverPhotoQuery, CaregiverPhotoFileContent>
{
    private readonly ICaregiversDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public GetCaregiverPhotoQueryHandler(
        ICaregiversDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<CaregiverPhotoFileContent>> Handle(
        GetCaregiverPhotoQuery request,
        CancellationToken cancellationToken)
    {
        string? avatarKey = await _dbContext.GetDiscoverableCaregiverAvatarKeyAsync(
            request.CaregiverId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(avatarKey))
        {
            return Result<CaregiverPhotoFileContent>.Failure(CaregiverPhotoErrors.NotFound);
        }

        Result<PrivateFileContent> file = await _fileStorage.OpenReadAsync(
            avatarKey,
            cancellationToken);

        if (file.IsFailure)
        {
            return Result<CaregiverPhotoFileContent>.Failure(CaregiverPhotoErrors.NotFound);
        }

        return Result<CaregiverPhotoFileContent>.Success(
            new CaregiverPhotoFileContent(file.Value.ContentType, file.Value.Content));
    }
}
