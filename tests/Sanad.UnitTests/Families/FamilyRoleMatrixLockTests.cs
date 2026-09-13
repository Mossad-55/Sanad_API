using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Application.Invitations;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

// Locks EXACTLY the v1 family role matrix (behavior verified correct; code
// intentionally untouched by SET-A1):
//   - invitations         : Owner or Editor
//   - bookings (create)   : Owner or Editor
//   - remove member       : Owner only
//   - change member role  : Owner only
public sealed class FamilyRoleMatrixLockTests
{
    private static readonly DateTime UtcNow =
        new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    private static Family SeedFamily(
        UserId owner,
        UserId editor,
        UserId viewer)
    {
        Family family = Family.Create(owner, "Role Matrix Family");

        family.AddMember(FamilyMember.Create(
            editor,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Editor));

        family.AddMember(FamilyMember.Create(
            viewer,
            owner,
            FamilyRelationshipType.Other,
            FamilyRole.Viewer));

        return family;
    }

    // ------------------------- Invitations lock -------------------------

    [Fact]
    public async Task Invite_Denied_ForViewer()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new CreateFamilyInvitationCommandHandler(
            db,
            new FakeIdentityGateway());

        Result<FamilyInvitationResponse> result =
            await handler.Handle(
                new CreateFamilyInvitationCommand(
                    viewer,
                    "invitee@example.com",
                    FamilyRole.Viewer,
                    FamilyRelationshipType.Other,
                    UtcNow),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyInvitationErrors.AccessDenied, result.Error);
    }

    [Fact]
    public async Task Invite_Allowed_ForEditor()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();
        UserId invitee = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new CreateFamilyInvitationCommandHandler(
            db,
            new FakeIdentityGateway(
                new FamilyInviteeAccount(
                    invitee,
                    Exists: true,
                    HasFamilyAccount: true)));

        Result<FamilyInvitationResponse> result =
            await handler.Handle(
                new CreateFamilyInvitationCommand(
                    editor,
                    "invitee@example.com",
                    FamilyRole.Viewer,
                    FamilyRelationshipType.Other,
                    UtcNow),
                CancellationToken.None);

        // Editor cleared the role gate (no AccessDenied) and the invitation
        // was created.
        Assert.True(result.IsSuccess);
    }

    // -------------------------- Bookings lock ---------------------------

    [Fact]
    public async Task CreateBooking_Denied_ForViewer()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        Elderly elderly = CreateElderly(family);

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        var handler = new CreateBookingCheckoutCommandHandler(
            db,
            new FakePricing(BookingCaregiverType.Medical, 500m));

        Result<BookingCheckoutResponse> result =
            await handler.Handle(
                CreateBookingCommand(viewer, elderly.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.UnauthorizedRole", result.Error.Code);
    }

    [Fact]
    public async Task CreateBooking_Allowed_ForEditor()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        Elderly elderly = CreateElderly(family);

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        var handler = new CreateBookingCheckoutCommandHandler(
            db,
            new FakePricing(BookingCaregiverType.Medical, 500m));

        Result<BookingCheckoutResponse> result =
            await handler.Handle(
                CreateBookingCommand(editor, elderly.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.PendingPayment, result.Value.Status);
    }

    // --------------------- Change role / remove lock --------------------

    [Fact]
    public async Task ChangeRole_Denied_ForEditor()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new ChangeFamilyMemberRoleCommandHandler(db);

        Result result =
            await handler.Handle(
                new ChangeFamilyMemberRoleCommand(
                    editor,
                    viewer,
                    FamilyRole.Editor),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotOwner, result.Error);
    }

    [Fact]
    public async Task ChangeRole_Denied_ForViewer()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new ChangeFamilyMemberRoleCommandHandler(db);

        Result result =
            await handler.Handle(
                new ChangeFamilyMemberRoleCommand(
                    viewer,
                    editor,
                    FamilyRole.Viewer),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotOwner, result.Error);
    }

    [Fact]
    public async Task ChangeRole_Allowed_ForOwner()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new ChangeFamilyMemberRoleCommandHandler(db);

        Result result =
            await handler.Handle(
                new ChangeFamilyMemberRoleCommand(
                    owner,
                    viewer,
                    FamilyRole.Editor),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FamilyRole.Editor, family.GetRole(viewer));
    }

    [Fact]
    public async Task RemoveMember_Denied_ForEditor()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new RemoveFamilyMemberCommandHandler(db);

        Result result =
            await handler.Handle(
                new RemoveFamilyMemberCommand(editor, viewer),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotOwner, result.Error);
    }

    [Fact]
    public async Task RemoveMember_Denied_ForViewer()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new RemoveFamilyMemberCommandHandler(db);

        Result result =
            await handler.Handle(
                new RemoveFamilyMemberCommand(viewer, editor),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotOwner, result.Error);
    }

    [Fact]
    public async Task RemoveMember_Allowed_ForOwner()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId viewer = UserId.New();

        Family family = SeedFamily(owner, editor, viewer);
        db.Families.Add(family);
        await db.SaveChangesAsync();

        var handler = new RemoveFamilyMemberCommandHandler(db);

        Result result =
            await handler.Handle(
                new RemoveFamilyMemberCommand(owner, viewer),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(family.GetRole(viewer));
    }

    // ------------------------------ Helpers -----------------------------

    private static Elderly CreateElderly(Family family) =>
        Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

    private static CreateBookingCheckoutCommand CreateBookingCommand(
        UserId caller,
        ElderlyId elderlyId) =>
        new(
            caller,
            elderlyId,
            CaregiverId.New(),
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(UtcNow).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            DateOnly.FromDateTime(UtcNow),
            UtcNow);

    private sealed class FakePricing(
        BookingCaregiverType type,
        decimal fee) : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Result<CaregiverBookingPrice>.Success(
                    new CaregiverBookingPrice(type, fee)));
    }

    private sealed class FakeIdentityGateway : IFamilyIdentityGateway
    {
        private readonly FamilyInviteeAccount? _invitee;

        public FakeIdentityGateway(FamilyInviteeAccount? invitee = null)
        {
            _invitee = invitee;
        }

        public Task<Result<FamilyInviteeAccount>>
            GetFamilyInviteeByEmailAsync(
                string email,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _invitee is null
                    ? Result<FamilyInviteeAccount>.Failure(
                        new Error(
                            "Identity.User.EmailNotFound",
                            "Not found."))
                    : Result<FamilyInviteeAccount>.Success(_invitee));

        public Task SendFamilyInvitationEmailAsync(
            string email,
            string familyName,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<FamilyMemberProfile>>
            GetFamilyMemberProfilesAsync(
                IReadOnlyCollection<UserId> userIds,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FamilyMemberProfile>>([]);

        public Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(
            string phoneNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(
            string arabicFullName,
            string englishFullName,
            string phoneNumber,
            Gender gender,
            DateOnly dateOfBirth,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteElderlyAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Result> AnonymizeFamilyAccountsAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}
