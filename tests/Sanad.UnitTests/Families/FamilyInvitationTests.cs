using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Application.Invitations;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

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

// ------------------- Sent-invitations outbox (handlers) -------------------

public sealed class FamilySentInvitationHandlerTests
{
    private static readonly DateTime UtcNow =
        new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    private static FamilyInvitation CreateSent(
        FamilyId familyId,
        string email,
        FamilyRole role,
        DateTime createdOnUtc)
    {
        (FamilyInvitation invitation, _) =
            FamilyInvitation.Create(
                familyId,
                Email.Create(email),
                UserId.New(),
                role,
                FamilyRelationshipType.Son,
                UserId.New(),
                createdOnUtc);

        return invitation;
    }

    [Fact]
    public async Task ListSent_ReturnsOnlyCallerFamily_WithExactlySevenFields()
    {
        await using FamiliesDbContext dbContext = CreateDbContext();

        UserId ownerA = UserId.New();
        UserId ownerB = UserId.New();
        Family familyA = Family.Create(ownerA, "Al-Mansour Family");
        Family familyB = Family.Create(ownerB, "Other Family");

        FamilyInvitation pending =
            CreateSent(
                familyA.Id,
                "sister@example.com",
                FamilyRole.Editor,
                UtcNow);
        FamilyInvitation revoked =
            CreateSent(
                familyA.Id,
                "brother@example.com",
                FamilyRole.Viewer,
                UtcNow.AddMinutes(-10));
        revoked.Revoke(UtcNow.AddMinutes(-5));
        FamilyInvitation expired =
            CreateSent(
                familyA.Id,
                "cousin@example.com",
                FamilyRole.Editor,
                UtcNow.AddDays(-30));
        FamilyInvitation otherFamily =
            CreateSent(
                familyB.Id,
                "outsider@example.com",
                FamilyRole.Viewer,
                UtcNow);

        dbContext.Families.AddRange(familyA, familyB);
        dbContext.Invitations.AddRange(pending, revoked, expired, otherFamily);
        await dbContext.SaveChangesAsync();

        var handler =
            new ListSentFamilyInvitationsQueryHandler(dbContext);

        Result<IReadOnlyList<FamilySentInvitationResponse>> result =
            await handler.Handle(
                new ListSentFamilyInvitationsQuery(ownerA, UtcNow),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);

        IReadOnlyList<FamilySentInvitationResponse> items = result.Value;

        // Exactly three family-A invitations, newest first; no cross-family
        // leak.
        Assert.Equal(3, items.Count);
        Assert.Equal(
            new[] { pending.Id, revoked.Id, expired.Id },
            items.Select(item => item.Id).ToArray());
        Assert.DoesNotContain(items, item => item.Id == otherFamily.Id);

        // The contract is exactly the seven named fields, in order.
        string[] expectedFields =
        {
            "Id",
            "InvitedEmail",
            "Role",
            "RelationshipType",
            "Status",
            "CreatedOnUtc",
            "ExpiresOnUtc"
        };
        Assert.Equal(
            expectedFields,
            typeof(FamilySentInvitationResponse)
                .GetProperties()
                .Select(property => property.Name)
                .ToArray());

        FamilySentInvitationResponse newest = items[0];
        Assert.Equal(pending.Id, newest.Id);
        Assert.Equal("sister@example.com", newest.InvitedEmail);
        Assert.Equal(FamilyRole.Editor, newest.Role);
        Assert.Equal(
            FamilyRelationshipType.Son,
            newest.RelationshipType);
        Assert.Equal(FamilyInvitationStatus.Pending, newest.Status);
        Assert.Equal(UtcNow, newest.CreatedOnUtc);
        Assert.Equal(UtcNow.AddDays(7), newest.ExpiresOnUtc);

        Assert.Equal(
            FamilyInvitationStatus.Revoked,
            items.Single(item => item.Id == revoked.Id).Status);
    }

    [Fact]
    public async Task ListSent_ReturnedPendingId_CancelsViaExistingRevokeEndpointOnly()
    {
        await using FamiliesDbContext dbContext = CreateDbContext();

        UserId ownerA = UserId.New();
        UserId ownerB = UserId.New();
        Family familyA = Family.Create(ownerA, "Al-Mansour Family");
        Family familyB = Family.Create(ownerB, "Other Family");

        FamilyInvitation pending =
            CreateSent(
                familyA.Id,
                "sister@example.com",
                FamilyRole.Editor,
                UtcNow);

        dbContext.Families.AddRange(familyA, familyB);
        dbContext.Invitations.Add(pending);
        await dbContext.SaveChangesAsync();

        var listHandler =
            new ListSentFamilyInvitationsQueryHandler(dbContext);
        var revokeHandler =
            new RevokeFamilyInvitationCommandHandler(dbContext);

        Result<IReadOnlyList<FamilySentInvitationResponse>> listed =
            await listHandler.Handle(
                new ListSentFamilyInvitationsQuery(ownerA, UtcNow),
                CancellationToken.None);

        FamilySentInvitationResponse returnedPending =
            listed.Value.Single(item =>
                item.Status == FamilyInvitationStatus.Pending);

        // The family owner cancels through the EXISTING revoke command —
        // the same one behind DELETE /api/v1/family/invitations/{id}.
        Result revoked =
            await revokeHandler.Handle(
                new RevokeFamilyInvitationCommand(
                    ownerA,
                    returnedPending.Id,
                    UtcNow.AddMinutes(1)),
                CancellationToken.None);

        Assert.True(revoked.IsSuccess);

        FamilyInvitation? persisted =
            await dbContext.Invitations
                .SingleOrDefaultAsync(
                    invitation => invitation.Id == pending.Id,
                    CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(
            FamilyInvitationStatus.Revoked,
            persisted!.Status);

        // The same endpoint stays family-scoped: another family's owner gets
        // the documented AccessDenied on that invitation id (no second cancel
        // endpoint needed).
        Result otherFamilyAttempt =
            await revokeHandler.Handle(
                new RevokeFamilyInvitationCommand(
                    ownerB,
                    returnedPending.Id,
                    UtcNow.AddMinutes(2)),
                CancellationToken.None);

        Assert.False(otherFamilyAttempt.IsSuccess);
        Assert.Equal(
            FamilyInvitationErrors.AccessDenied,
            otherFamilyAttempt.Error);
    }

    [Fact]
    public async Task ListMy_StillReturnsOnlyPendingUnexpiredReceivedInvitations()
    {
        await using FamiliesDbContext dbContext = CreateDbContext();

        UserId inviter = UserId.New();
        UserId invitee = UserId.New();
        UserId stranger = UserId.New();
        Family family = Family.Create(inviter, "Al-Mansour Family");

        FamilyInvitation pending =
            FamilyInvitation.Create(
                family.Id,
                Email.Create("sister@example.com"),
                invitee,
                FamilyRole.Editor,
                FamilyRelationshipType.Son,
                inviter,
                UtcNow).Invitation;
        (FamilyInvitation expired, _) =
            FamilyInvitation.Create(
                family.Id,
                Email.Create("old@example.com"),
                invitee,
                FamilyRole.Viewer,
                FamilyRelationshipType.Son,
                inviter,
                UtcNow.AddDays(-9));
        (FamilyInvitation accepted, _) =
            FamilyInvitation.Create(
                family.Id,
                Email.Create("done@example.com"),
                invitee,
                FamilyRole.Viewer,
                FamilyRelationshipType.Son,
                inviter,
                UtcNow.AddDays(-1));
        accepted.Accept(invitee, UtcNow.AddHours(-1));
        // Sent by the family but addressed to a third user: must NOT appear
        // in the invitee's received list.
        FamilyInvitation receivedByOther =
            FamilyInvitation.Create(
                family.Id,
                Email.Create("other@example.com"),
                stranger,
                FamilyRole.Viewer,
                FamilyRelationshipType.Son,
                inviter,
                UtcNow).Invitation;

        dbContext.Families.Add(family);
        dbContext.Invitations.AddRange(pending, expired, accepted, receivedByOther);
        await dbContext.SaveChangesAsync();

        var handler = new ListMyFamilyInvitationsQueryHandler(dbContext);

        Result<IReadOnlyList<FamilyInvitationResponse>> result =
            await handler.Handle(
                new ListMyFamilyInvitationsQuery(invitee, UtcNow),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        FamilyInvitationResponse item =
            Assert.Single(result.Value);

        Assert.Equal(pending.Id, item.Id);
        Assert.Equal(family.Id, item.FamilyId);
        Assert.Equal("Al-Mansour Family", item.FamilyName);
        Assert.Equal("sister@example.com", item.InvitedEmail);
        Assert.Equal(FamilyInvitationStatus.Pending, item.Status);
        Assert.Equal(UtcNow, item.CreatedOnUtc);
    }
}