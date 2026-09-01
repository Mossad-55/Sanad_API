using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.Elderlies;

public static class DependentPhotoStorage
{
    public const string Folder = "elderly-photos";
}

public sealed record DependentPhotoContent(
    string FileName,
    string ContentType,
    Stream Content);

// --------------------------- Set / replace ----------------------------

public sealed record SetDependentPhotoCommand(
    UserId OwnerUserId,
    ElderlyId DependentId,
    string PhotoKey)
    : ICommand<DependentResponse>;

public sealed class SetDependentPhotoCommandValidator
    : AbstractValidator<SetDependentPhotoCommand>
{
    public SetDependentPhotoCommandValidator()
    {
        RuleFor(c => c.OwnerUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.PhotoKey)
            .NotEmpty()
            .MaximumLength(Elderly.MaximumProfileImageKeyLength);
    }
}

public sealed class SetDependentPhotoCommandHandler
    : ICommandHandler<SetDependentPhotoCommand, DependentResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public SetDependentPhotoCommandHandler(
        IFamiliesDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<DependentResponse>> Handle(
        SetDependentPhotoCommand request,
        CancellationToken cancellationToken)
    {
        Elderly? elderly =
            await _dbContext.Elderlies
                .SingleOrDefaultAsync(
                    e => e.Id == request.DependentId &&
                         e.OwnerUserId == request.OwnerUserId,
                    cancellationToken);

        if (elderly is null)
        {
            return ElderlyErrors.NotFound;
        }

        string? previousKey = elderly.ProfileImageKey;

        elderly.ChangePhoto(request.PhotoKey);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Best-effort orphan cleanup of the replaced photo.
        if (!string.IsNullOrWhiteSpace(previousKey) &&
            previousKey != request.PhotoKey)
        {
            await _fileStorage.DeleteAsync(
                previousKey,
                cancellationToken);
        }

        return elderly.ToResponse();
    }
}

// ------------------------------ Download ------------------------------

public sealed record GetDependentPhotoQuery(
    UserId OwnerUserId,
    ElderlyId DependentId)
    : IQuery<DependentPhotoContent>;

public sealed class GetDependentPhotoQueryValidator
    : AbstractValidator<GetDependentPhotoQuery>
{
    public GetDependentPhotoQueryValidator()
    {
        RuleFor(c => c.OwnerUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
    }
}

public sealed class GetDependentPhotoQueryHandler
    : IQueryHandler<GetDependentPhotoQuery, DependentPhotoContent>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public GetDependentPhotoQueryHandler(
        IFamiliesDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<DependentPhotoContent>> Handle(
        GetDependentPhotoQuery request,
        CancellationToken cancellationToken)
    {
        Elderly? elderly =
            await _dbContext.Elderlies
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    e => e.Id == request.DependentId &&
                         e.OwnerUserId == request.OwnerUserId,
                    cancellationToken);

        if (elderly is null ||
            string.IsNullOrWhiteSpace(elderly.ProfileImageKey))
        {
            return Result<DependentPhotoContent>.Failure(
                ElderlyErrors.NotFound);
        }

        Result<PrivateFileContent> file =
            await _fileStorage.OpenReadAsync(
                elderly.ProfileImageKey,
                cancellationToken);

        if (file.IsFailure)
        {
            return Result<DependentPhotoContent>.Failure(
                file.Error);
        }

        string extension =
            file.Value.ContentType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".bin"
            };

        string fileName =
            $"dependent-{elderly.Id.Value:N}{extension}";

        return new DependentPhotoContent(
            fileName,
            file.Value.ContentType,
            file.Value.Content);
    }
}