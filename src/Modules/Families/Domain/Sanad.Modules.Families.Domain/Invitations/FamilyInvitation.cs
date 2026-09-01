using System.Security.Cryptography;
using System.Text;
using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Domain.Invitations;

public sealed class FamilyInvitation :
    AggregateRoot<FamilyInvitationId>
{
    public const int MaximumTokenHashLength = 128;

    public static readonly TimeSpan TokenLifetime =
        TimeSpan.FromDays(7);

    private FamilyInvitation()
    {
    }

    private FamilyInvitation(
        FamilyInvitationId id,
        FamilyId familyId,
        Email invitedEmail,
        UserId invitedUserId,
        FamilyRole role,
        FamilyRelationshipType relationshipType,
        string tokenHash,
        UserId createdByUserId,
        DateTime createdOnUtc,
        DateTime expiresOnUtc)
        : base(id)
    {
        FamilyId = familyId;
        InvitedEmail = invitedEmail;
        InvitedUserId = invitedUserId;
        Role = role;
        RelationshipType = relationshipType;
        TokenHash = tokenHash;
        CreatedByUserId = createdByUserId;
        CreatedOnUtc = createdOnUtc;
        ExpiresOnUtc = expiresOnUtc;
        Status = FamilyInvitationStatus.Pending;
    }

    public FamilyId FamilyId { get; private set; }

    public Email InvitedEmail { get; private set; } = default!;

    public UserId InvitedUserId { get; private set; }

    public FamilyRole Role { get; private set; }

    public FamilyRelationshipType RelationshipType { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public FamilyInvitationStatus Status { get; private set; }

    public UserId CreatedByUserId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime ExpiresOnUtc { get; private set; }

    public DateTime? DecidedOnUtc { get; private set; }

    /// <summary>
    /// Creates a pending invitation. The plaintext token is returned once
    /// (it goes into the email link); only its hash is persisted.
    /// </summary>
    public static (FamilyInvitation Invitation, string PlainToken) Create(
        FamilyId familyId,
        Email invitedEmail,
        UserId invitedUserId,
        FamilyRole role,
        FamilyRelationshipType relationshipType,
        UserId createdByUserId,
        DateTime utcNow)
    {
        if (role is FamilyRole.Owner)
        {
            throw new DomainException(
                "An invited member cannot be assigned the Owner role.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new DomainException("Role is invalid.");
        }

        if (!Enum.IsDefined(relationshipType))
        {
            throw new DomainException(
                "Relationship type is invalid.");
        }

        if (invitedUserId == UserId.Empty)
        {
            throw new DomainException(
                "Invited user is required.");
        }

        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Operation time must be in UTC.");
        }

        string plainToken = GeneratePlainToken();

        var invitation = new FamilyInvitation(
            FamilyInvitationId.New(),
            familyId,
            invitedEmail,
            invitedUserId,
            role,
            relationshipType,
            HashToken(plainToken),
            createdByUserId,
            utcNow,
            utcNow.Add(TokenLifetime));

        return (invitation, plainToken);
    }

    public void Accept(
        UserId actingUserId,
        DateTime utcNow)
    {
        EnsurePendingDecision(
            actingUserId,
            utcNow);

        Status = FamilyInvitationStatus.Accepted;
        DecidedOnUtc = utcNow;
    }

    public void Decline(
        UserId actingUserId,
        DateTime utcNow)
    {
        EnsurePendingDecision(
            actingUserId,
            utcNow);

        Status = FamilyInvitationStatus.Declined;
        DecidedOnUtc = utcNow;
    }

    public void Revoke(DateTime utcNow)
    {
        EnsurePending(utcNow);

        Status = FamilyInvitationStatus.Revoked;
        DecidedOnUtc = utcNow;
    }

    public bool IsExpired(DateTime utcNow) =>
        Status == FamilyInvitationStatus.Pending &&
        utcNow > ExpiresOnUtc;

    public static string HashToken(string plainToken)
    {
        byte[] hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(plainToken));

        return Convert.ToHexString(hash);
    }

    private void EnsurePendingDecision(
        UserId actingUserId,
        DateTime utcNow)
    {
        if (actingUserId != InvitedUserId)
        {
            throw new DomainException(
                "Only the invited user can respond to this invitation.");
        }

        EnsurePending(utcNow);
    }

    private void EnsurePending(DateTime utcNow)
    {
        if (Status != FamilyInvitationStatus.Pending)
        {
            throw new DomainException(
                "This invitation is no longer pending.");
        }

        if (utcNow > ExpiresOnUtc)
        {
            Status = FamilyInvitationStatus.Expired;
            DecidedOnUtc = utcNow;

            throw new DomainException(
                "This invitation has expired.");
        }
    }

    private static string GeneratePlainToken()
    {
        // Opaque URL-safe token, mirroring the refresh-token generation
        // in Identity's JwtAuthTokenService.
        byte[] tokenBytes =
            RandomNumberGenerator.GetBytes(32);

        return Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}