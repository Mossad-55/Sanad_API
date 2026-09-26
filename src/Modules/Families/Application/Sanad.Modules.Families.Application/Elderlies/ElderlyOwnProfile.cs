using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Assessments;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.Elderlies;

public sealed record ElderlyOwnProfileResponse(
    ElderlyId Id,
    string ArabicFullName,
    string EnglishFullName,
    int Age,
    bool HasPhoto,
    string PhotoUrl,
    string TimeZoneId,
    FamilyAssessmentResultResponse? LatestAssessment,
    DateTime UpdatedOnUtc);

public sealed record GetOwnElderlyProfileQuery(UserId UserId)
    : IQuery<ElderlyOwnProfileResponse>;

public sealed class GetOwnElderlyProfileQueryHandler(IFamiliesDbContext dbContext)
    : IQueryHandler<GetOwnElderlyProfileQuery, ElderlyOwnProfileResponse>
{
    public async Task<Result<ElderlyOwnProfileResponse>> Handle(
        GetOwnElderlyProfileQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == UserId.Empty)
            return ElderlyErrors.NotFound;

        Elderly? elderly = await dbContext.Elderlies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdentityUserId == request.UserId, cancellationToken);
        if (elderly is null)
            return ElderlyErrors.NotFound;

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                ElderlyTimeZone.Normalize(elderly.TimeZoneId));
        }
        catch (Exception exception) when (exception is DomainException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return ElderlyErrors.InvalidProfile;
        }

        DateOnly localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
        int age = localToday.Year - elderly.DateOfBirth.Year;
        if (elderly.DateOfBirth > localToday.AddYears(-age))
            age--;

        CareAssessment? latest = await dbContext.CareAssessments.AsNoTracking()
            .Where(x => x.ElderlyId == elderly.Id && x.FamilyId == elderly.FamilyId)
            .OrderByDescending(x => x.CompletedOnUtc)
            .ThenByDescending(x => x.Id.Value)
            .FirstOrDefaultAsync(cancellationToken);

        FamilyAssessmentResultResponse? assessment = null;
        if (latest is not null)
        {
            AssessmentTier? tier = await dbContext.AssessmentTiers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == latest.AssessmentTierId, cancellationToken);
            if (tier is not null)
            {
                assessment = new FamilyAssessmentResultResponse(
                    latest.Id,
                    latest.TotalScore,
                    tier.ToFamilyResponse(),
                    latest.CompletedOnUtc);
            }
        }

        return new ElderlyOwnProfileResponse(
            elderly.Id,
            elderly.ArabicFullName,
            elderly.EnglishFullName,
            age,
            !string.IsNullOrWhiteSpace(elderly.ProfileImageKey),
            "/api/v1/elderly/profile/photo",
            elderly.TimeZoneId,
            assessment,
            elderly.UpdatedOnUtc);
    }
}

public sealed record GetOwnElderlyProfilePhotoQuery(UserId UserId)
    : IQuery<DependentPhotoContent>;

public sealed class GetOwnElderlyProfilePhotoQueryHandler(
    IFamiliesDbContext dbContext,
    IFileStorage fileStorage)
    : IQueryHandler<GetOwnElderlyProfilePhotoQuery, DependentPhotoContent>
{
    public async Task<Result<DependentPhotoContent>> Handle(
        GetOwnElderlyProfilePhotoQuery request,
        CancellationToken cancellationToken)
    {
        Elderly? elderly = await dbContext.Elderlies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdentityUserId == request.UserId, cancellationToken);
        if (elderly is null || string.IsNullOrWhiteSpace(elderly.ProfileImageKey))
            return ElderlyErrors.NotFound;

        Result<PrivateFileContent> file = await fileStorage.OpenReadAsync(
            elderly.ProfileImageKey,
            cancellationToken);
        if (file.IsFailure)
            return Result<DependentPhotoContent>.Failure(file.Error);

        string extension = file.Value.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".bin"
        };

        return new DependentPhotoContent(
            $"elderly-{elderly.Id.Value:N}{extension}",
            file.Value.ContentType,
            file.Value.Content);
    }
}
