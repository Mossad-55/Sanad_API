using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;

namespace Sanad.UnitTests.Families;

public sealed class FamilyInvitationTests
{
    private static readonly DateTime UtcNow =
        new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static FamilyInvitation CreatePending(
        FamilyRole role = FamilyRole.Editor)
    {
        (FamilyInvitation invitation, _) =
            FamilyInvitation.Create(
                FamilyId.New(),
                Email.Create("member@example.com"),
                UserId.New(),
                role,
                FamilyRelationshipType.Son,
                UserId.New(),
                UtcNow);

        return invitation;
    }

    [Fact]
    public void Create_DefaultsToPendingWithSevenDayExpiry()
    {
        FamilyInvitation invitation = CreatePending();

        Assert.Equal(
            FamilyInvitationStatus.Pending,
            invitation.Status);

        Assert.Equal(
            UtcNow.AddDays(7),
            invitation.ExpiresOnUtc);
    }

    [Fact]
    public void Create_RejectsOwnerRole()
    {
        Assert.Throws<DomainException>(
            () => FamilyInvitation.Create(
                FamilyId.New(),
                Email.Create("member@example.com"),
                UserId.New(),
                FamilyRole.Owner,
                FamilyRelationshipType.Son,
                UserId.New(),
                UtcNow));
    }

    [Fact]
    public void Create_ReturnsVerifiablePlainTokenAndHash()
    {
        (FamilyInvitation invitation, string plainToken) =
            FamilyInvitation.Create(
                FamilyId.New(),
                Email.Create("member@example.com"),
                UserId.New(),
                FamilyRole.Viewer,
                FamilyRelationshipType.Daughter,
                UserId.New(),
                UtcNow);

        Assert.False(string.IsNullOrWhiteSpace(plainToken));
        Assert.Equal(
            FamilyInvitation.HashToken(plainToken),
            invitation.TokenHash);
    }

    [Fact]
    public void Accept_ByInvitee_MovesToAccepted()
    {
        FamilyInvitation invitation = CreatePending();
        UserId invitee = invitation.InvitedUserId;

        invitation.Accept(invitee, UtcNow.AddHours(1));

        Assert.Equal(
            FamilyInvitationStatus.Accepted,
            invitation.Status);
    }

    [Fact]
    public void Accept_ByOtherUser_IsRejected()
    {
        FamilyInvitation invitation = CreatePending();

        Assert.Throws<DomainException>(
            () => invitation.Accept(
                UserId.New(),
                UtcNow.AddHours(1)));
    }

    [Fact]
    public void Accept_AfterExpiry_IsRejectedAndMarksExpired()
    {
        FamilyInvitation invitation = CreatePending();
        UserId invitee = invitation.InvitedUserId;

        Assert.Throws<DomainException>(
            () => invitation.Accept(
                invitee,
                UtcNow.AddDays(8)));

        Assert.Equal(
            FamilyInvitationStatus.Expired,
            invitation.Status);
    }
}