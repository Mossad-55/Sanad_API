using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Domain.Posts;

namespace Ahlena.Modules.Community.Application.Posts;

/// <summary>
/// FUR-07 report intake. Reports apply to published posts only; a normal
/// authenticated Community user can report a published post but not their own
/// post. Anonymous reports are not accepted (the route requires a JWT), and a
/// repeat request returns the existing retained report without creating
/// another row.
/// </summary>
public sealed record SubmitCommunityPostReportCommand(
    Guid PostId,
    UserId ReporterId,
    string? ReasonCode,
    string? Note) : ICommand<SubmitCommunityPostReportResponse>;

public sealed record SubmitCommunityPostReportResponse(Guid ReportId, bool Created);

public sealed class SubmitCommunityPostReportCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<SubmitCommunityPostReportCommand, SubmitCommunityPostReportResponse>
{
    public async Task<Result<SubmitCommunityPostReportResponse>> Handle(
        SubmitCommunityPostReportCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ReporterId == UserId.Empty)
            return Result<SubmitCommunityPostReportResponse>.Failure(
                CommunityModerationErrors.InvalidReportReason);

        var postId = new CommunityPostId(request.PostId);
        var post = await dbContext.Posts
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == postId, cancellationToken);

        if (post is null)
            return Result<SubmitCommunityPostReportResponse>.Failure(
                new Error("Community.PostNotFound", "Community post not found."));

        if (post.Status != PostStatus.Published)
            return Result<SubmitCommunityPostReportResponse>.Failure(
                CommunityModerationErrors.ReportNotAllowed);

        if (post.AuthorId == request.ReporterId)
            return Result<SubmitCommunityPostReportResponse>.Failure(
                CommunityModerationErrors.CannotReportOwnPost);

        if (!CommunityReportReasonCodes.IsValid(request.ReasonCode))
            return Result<SubmitCommunityPostReportResponse>.Failure(
                CommunityModerationErrors.InvalidReportReason);

        string? normalizedNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (CommunityReportReasonCodes.RequiresNote(request.ReasonCode) && normalizedNote is null)
            return Result<SubmitCommunityPostReportResponse>.Failure(
                CommunityModerationErrors.InvalidReportReason);

        var existing = await dbContext.CommunityPostReports
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.PostId == postId && r.ReporterId == request.ReporterId,
                cancellationToken);

        if (existing is not null)
            return Result<SubmitCommunityPostReportResponse>.Success(
                new SubmitCommunityPostReportResponse(existing.Id.Value, Created: false));

        CommunityPostReport report;
        try
        {
            report = CommunityPostReport.Create(
                postId, request.ReporterId, request.ReasonCode, normalizedNote, DateTime.UtcNow);
        }
        catch (DomainException)
        {
            return Result<SubmitCommunityPostReportResponse>.Failure(
                CommunityModerationErrors.InvalidReportReason);
        }

        dbContext.CommunityPostReports.Add(report);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            CommunityPostReportPersistenceErrors.IsReporterPostDuplicate(exception))
        {
            // The unique index is the final arbiter when two requests race
            // past the read-before-insert check. Clear the failed Added entry,
            // then return the row committed by the winning request.
            if (dbContext is DbContext context)
                context.Entry(report).State = EntityState.Detached;

            CommunityPostReport? concurrent = await dbContext.CommunityPostReports
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    r => r.PostId == postId && r.ReporterId == request.ReporterId,
                    cancellationToken);

            if (concurrent is null)
                throw;

            return Result<SubmitCommunityPostReportResponse>.Success(
                new SubmitCommunityPostReportResponse(concurrent.Id.Value, Created: false));
        }

        return Result<SubmitCommunityPostReportResponse>.Success(
            new SubmitCommunityPostReportResponse(report.Id.Value, Created: true));
    }
}

/// <summary>
/// Identifies only the unique retained-reporter/post constraint. Reflection
/// keeps the Application layer independent of PostgreSQL and SQLite provider
/// packages while retaining provider-specific error precision.
/// </summary>
internal static class CommunityPostReportPersistenceErrors
{
    private const string ReporterPostIndex = "IX_CommunityPostReports_PostId_ReporterId";

    public static bool IsReporterPostDuplicate(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            Type type = current.GetType();
            string? sqlState = type.GetProperty("SqlState")?.GetValue(current) as string;
            string? constraintName = type.GetProperty("ConstraintName")?.GetValue(current) as string;
            if (sqlState == "23505" && constraintName == ReporterPostIndex)
                return true;

            if (type.FullName == "Microsoft.Data.Sqlite.SqliteException" &&
                ReadIntProperty(type, current, "SqliteExtendedErrorCode") == 2067 &&
                current.Message.Contains("CommunityPostReports.PostId", StringComparison.Ordinal) &&
                current.Message.Contains("CommunityPostReports.ReporterId", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int? ReadIntProperty(Type type, Exception exception, string propertyName)
    {
        object? value = type.GetProperty(propertyName)?.GetValue(exception);
        return value is int integer ? integer : null;
    }
}

/// <summary>
/// FUR-07 report resolution. A moderator resolves an open report as
/// <c>ActionTaken</c> or <c>Dismissed</c> with actor/time audit and an
/// optional note. Resolution does not itself publish/reject content or
/// restrict an author.
/// </summary>
public sealed record ResolveCommunityPostReportCommand(
    Guid ReportId,
    UserId ModeratorId,
    string? Resolution,
    string? Note) : ICommand<bool>;

public sealed class ResolveCommunityPostReportCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<ResolveCommunityPostReportCommand, bool>
{
    public async Task<Result<bool>> Handle(
        ResolveCommunityPostReportCommand request,
        CancellationToken cancellationToken)
    {
        if (!TryParseResolution(request.Resolution, out CommunityReportStatus resolution))
            return Result<bool>.Failure(CommunityModerationErrors.InvalidReportResolution);

        if (request.Note?.Trim().Length > CommunityPostReport.MaximumNoteLength)
            return Result<bool>.Failure(CommunityModerationErrors.InvalidReportResolutionNote);

        var report = await dbContext.CommunityPostReports
            .SingleOrDefaultAsync(r => r.Id == new CommunityPostReportId(request.ReportId), cancellationToken);

        if (report is null)
            return Result<bool>.Failure(CommunityModerationErrors.ReportNotFound);

        try
        {
            report.Resolve(resolution, request.ModeratorId, request.Note, DateTime.UtcNow);
        }
        catch (DomainException)
        {
            return Result<bool>.Failure(CommunityModerationErrors.ReportAlreadyResolved);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (dbContext is DbContext context)
                context.ChangeTracker.Clear();

            return Result<bool>.Failure(CommunityModerationErrors.ReportAlreadyResolved);
        }

        return Result<bool>.Success(true);
    }

    private static bool TryParseResolution(string? value, out CommunityReportStatus resolution)
    {
        if (value is null)
        {
            resolution = default;
            return false;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out CommunityReportStatus parsed) &&
            parsed is CommunityReportStatus.ActionTaken or CommunityReportStatus.Dismissed)
        {
            resolution = parsed;
            return true;
        }

        resolution = default;
        return false;
    }
}
