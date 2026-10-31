using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Community.Domain.Posts;

/// <summary>
/// Typed identifier for a retained Community author restriction.
/// </summary>
public readonly record struct CommunityAuthorRestrictionId(Guid Value)
{
    public static CommunityAuthorRestrictionId Empty => new(Guid.Empty);
    public static CommunityAuthorRestrictionId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Fixed FUR-07 restriction presets. The contract does not define custom
/// durations; callers select one of these three values.
/// </summary>
public enum CommunityRestrictionDuration
{
    Hours24 = 0,
    Days7 = 1,
    Days30 = 2
}

public static class CommunityRestrictionDurationExtensions
{
    public static TimeSpan ToTimeSpan(this CommunityRestrictionDuration duration) =>
        duration switch
        {
            CommunityRestrictionDuration.Hours24 => TimeSpan.FromHours(24),
            CommunityRestrictionDuration.Days7 => TimeSpan.FromDays(7),
            CommunityRestrictionDuration.Days30 => TimeSpan.FromDays(30),
            _ => throw new ArgumentOutOfRangeException(nameof(duration))
        };
}

/// <summary>
/// The closed set of restriction reason codes. The FUR-07 contract enumerates
/// the rejection and report reason sets but not a separate restriction set;
/// this reuses the approved moderator-action (rejection) vocabulary rather
/// than inventing new codes. <see cref="Other"/> requires a note.
/// </summary>
public static class CommunityRestrictionReasonCodes
{
    public const string CommunityGuidelines = CommunityRejectionReasonCodes.CommunityGuidelines;
    public const string Spam = CommunityRejectionReasonCodes.Spam;
    public const string Duplicate = CommunityRejectionReasonCodes.Duplicate;
    public const string UnsafeContent = CommunityRejectionReasonCodes.UnsafeContent;
    public const string Other = CommunityRejectionReasonCodes.Other;

    public static bool IsValid(string? reasonCode) =>
        CommunityRejectionReasonCodes.IsValid(reasonCode);

    public static bool RequiresNote(string? reasonCode) =>
        CommunityRejectionReasonCodes.RequiresNote(reasonCode);
}

/// <summary>
/// A retained Community-only timed restriction on an author. It blocks new
/// posts, comments, and replies until a UTC expiry; existing content and all
/// non-write interactions remain available. It never touches Identity status,
/// login, sessions, or any other module. At most one active restriction per
/// author is enforced by the command handler, and a moderator can end one
/// early while preserving the original action and revocation audit.
/// </summary>
public sealed class CommunityAuthorRestriction : AggregateRoot<CommunityAuthorRestrictionId>
{
    public const int MaximumNoteLength = 1000;
    public const int MaximumRevocationReasonLength = 1000;
    public const int MaximumReasonCodeLength = 50;

    private CommunityAuthorRestriction()
    {
    }

    private CommunityAuthorRestriction(
        CommunityAuthorRestrictionId id,
        UserId authorId,
        UserId moderatorId,
        CommunityRestrictionDuration duration,
        string reasonCode,
        string? note,
        DateTime startsOnUtc,
        DateTime expiresOnUtc)
        : base(id)
    {
        AuthorId = authorId;
        ModeratorId = moderatorId;
        Duration = duration;
        ReasonCode = reasonCode;
        Note = note;
        StartsOnUtc = startsOnUtc;
        ExpiresOnUtc = expiresOnUtc;
    }

    public UserId AuthorId { get; private set; }
    public UserId ModeratorId { get; private set; }
    public CommunityRestrictionDuration Duration { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public DateTime StartsOnUtc { get; private set; }
    public DateTime ExpiresOnUtc { get; private set; }
    public UserId? RevokedBy { get; private set; }
    public DateTime? RevokedOnUtc { get; private set; }
    public string? RevocationReason { get; private set; }

    public static CommunityAuthorRestriction Create(
        UserId authorId,
        UserId moderatorId,
        CommunityRestrictionDuration duration,
        string? reasonCode,
        string? note,
        DateTime startsOnUtc)
    {
        if (authorId == UserId.Empty)
            throw new DomainException("A Community restriction requires an author.");

        if (moderatorId == UserId.Empty)
            throw new DomainException("A Community restriction requires a moderator.");

        if (!CommunityRestrictionReasonCodes.IsValid(reasonCode))
            throw new DomainException("Community restriction reason code is invalid.");

        string code = reasonCode!;
        string? normalizedNote = NormalizeNote(note);

        if (CommunityRestrictionReasonCodes.RequiresNote(code) && normalizedNote is null)
            throw new DomainException("A note is required for this restriction reason.");

        if (startsOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community restriction timestamps must be UTC.");

        return new CommunityAuthorRestriction(
            CommunityAuthorRestrictionId.New(),
            authorId,
            moderatorId,
            duration,
            code,
            normalizedNote,
            startsOnUtc,
            startsOnUtc.Add(duration.ToTimeSpan()));
    }

    /// <summary>
    /// True while the restriction has not been revoked and has not yet
    /// expired at the supplied UTC instant. Expiry is enforced by comparing
    /// UTC timestamps; no background job is involved.
    /// </summary>
    public bool IsActiveAt(DateTime utcNow) =>
        RevokedOnUtc is null && ExpiresOnUtc > utcNow;

    /// <summary>
    /// Ends the restriction early, preserving the original action and
    /// recording the revocation actor/time and required reason.
    /// </summary>
    public void Revoke(UserId moderatorId, string? reason, DateTime revokedOnUtc)
    {
        if (RevokedOnUtc is not null)
            throw new DomainException("Community restriction is already revoked.");

        if (moderatorId == UserId.Empty)
            throw new DomainException("A restriction revocation moderator is required.");

        string normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length == 0 ||
            normalizedReason.Length > MaximumRevocationReasonLength)
            throw new DomainException(
                $"A restriction revocation reason between 1 and {MaximumRevocationReasonLength} characters is required.");

        if (revokedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community restriction timestamps must be UTC.");

        RevokedBy = moderatorId;
        RevokedOnUtc = revokedOnUtc;
        RevocationReason = normalizedReason;
    }

    private static string? NormalizeNote(string? value)
    {
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > MaximumNoteLength)
            throw new DomainException(
                $"Community restriction notes must not exceed {MaximumNoteLength} characters.");
        return normalized;
    }
}
