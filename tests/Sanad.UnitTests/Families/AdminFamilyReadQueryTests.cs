using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// F6 minimal operational family read. Every case runs against a relational
/// (SQLite) Families store on purpose: the list paging, the exact-id/name
/// search, the member Include and the dependent filter are the parts a
/// relational translation can reject, and the read must translate there too.
/// The projection is asserted to publish only the pinned identity/association
/// facts: no dependent name, address, phone, health note, medical profile,
/// member display name, email, avatar or deletion reason/message may appear.
/// </summary>
public sealed class AdminFamilyReadQueryTests
{
    [Fact]
    public async Task List_clamps_paging_and_reports_the_filtered_total()
    {
        await using TestFamilyDb db = await TestFamilyDb.CreateAsync();
        Family[] seeded =
        [
            Family.Create(UserId.New(), "Al Noor Family"),
            Family.Create(UserId.New(), "Sunrise Care"),
            Family.Create(UserId.New(), "Al Noor Extended")
        ];
        db.Context.Families.AddRange(seeded);
        await db.Context.SaveChangesAsync();

        var handler = new ListAdminFamiliesQueryHandler(db.Context);

        var defaults = await handler.Handle(new(Page: 0, PageSize: 0), default);
        var negative = await handler.Handle(new(Page: -4, PageSize: -10), default);
        var oversized = await handler.Handle(new(PageSize: 500), default);

        Assert.True(defaults.IsSuccess);
        Assert.Equal(1, defaults.Value.Page);
        Assert.Equal(25, defaults.Value.PageSize);
        Assert.Equal(3, defaults.Value.TotalCount);
        Assert.Equal(3, defaults.Value.Items.Count);
        Assert.Equal(1, negative.Value.Page);
        Assert.Equal(25, negative.Value.PageSize);
        Assert.Equal(100, oversized.Value.PageSize);

        // Ordering is non-increasing by creation instant; the id tie-break keeps
        // the order stable when two rows share one stored instant.
        Assert.True(defaults.Value.Items
            .Zip(defaults.Value.Items.Skip(1))
            .All(pair => pair.First.CreatedOnUtc >= pair.Second.CreatedOnUtc));
    }

    [Fact]
    public async Task List_pages_stably_without_omitting_or_repeating_a_family()
    {
        await using TestFamilyDb db = await TestFamilyDb.CreateAsync();
        Family[] seeded = [.. Enumerable.Range(0, 3).Select(index => Family.Create(UserId.New(), $"Family {index}"))];
        db.Context.Families.AddRange(seeded);
        await db.Context.SaveChangesAsync();

        var handler = new ListAdminFamiliesQueryHandler(db.Context);
        var first = await handler.Handle(new(Page: 1, PageSize: 2), default);
        var second = await handler.Handle(new(Page: 2, PageSize: 2), default);
        var beyond = await handler.Handle(new(Page: 3, PageSize: 2), default);

        Assert.Equal(2, first.Value.Items.Count);
        Assert.Single(second.Value.Items);
        Assert.Empty(beyond.Value.Items);
        Assert.Equal(3, first.Value.TotalCount);
        Assert.Equal(3, second.Value.TotalCount);

        Guid[] paged = [.. first.Value.Items.Select(item => item.FamilyId), .. second.Value.Items.Select(item => item.FamilyId)];
        Assert.Equal(3, paged.Distinct().Count());
        Assert.Equal(seeded.Select(family => family.Id.Value).OrderBy(id => id), paged.OrderBy(id => id));
        Assert.Equal(3, beyond.Value.TotalCount);
    }

    [Fact]
    public async Task List_searches_by_exact_id_and_by_name_and_never_errors_on_an_empty_match()
    {
        await using TestFamilyDb db = await TestFamilyDb.CreateAsync();
        Family family = Family.Create(UserId.New(), "Al Noor Family");
        Family other = Family.Create(UserId.New(), "Sunrise Care");
        db.Context.Families.AddRange(family, other);
        await db.Context.SaveChangesAsync();

        var handler = new ListAdminFamiliesQueryHandler(db.Context);

        var byExactId = await handler.Handle(new(Search: family.Id.Value.ToString()), default);
        var bySubstring = await handler.Handle(new(Search: "noor"), default);
        var byUpper = await handler.Handle(new(Search: "NOOR"), default);
        var blank = await handler.Handle(new(Search: "   "), default);
        var unknownName = await handler.Handle(new(Search: "no-such-family"), default);
        var unknownId = await handler.Handle(new(Search: Guid.NewGuid().ToString()), default);

        Assert.True(byExactId.IsSuccess);
        Assert.Equal(family.Id.Value, Assert.Single(byExactId.Value.Items).FamilyId);
        Assert.Equal(family.Id.Value, Assert.Single(bySubstring.Value.Items).FamilyId);
        Assert.Equal(family.Id.Value, Assert.Single(byUpper.Value.Items).FamilyId);
        Assert.Equal(2, blank.Value.Items.Count);

        foreach (var empty in new[] { unknownName, unknownId })
        {
            Assert.True(empty.IsSuccess);
            Assert.Empty(empty.Value.Items);
            Assert.Equal(0, empty.Value.TotalCount);
        }
    }

    [Fact]
    public async Task List_retains_a_deleted_family_without_publishing_its_deletion_reason_or_message()
    {
        await using TestFamilyDb db = await TestFamilyDb.CreateAsync();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Deletion candidate");
        family.AddMember(FamilyMember.Create(UserId.New(), owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        db.Context.Families.Add(family);
        await db.Context.SaveChangesAsync();

        family.MarkDeleted("SECRET_REASON", "SECRET_MESSAGE");
        await db.Context.SaveChangesAsync();

        var result = await new ListAdminFamiliesQueryHandler(db.Context)
            .Handle(new(Search: family.Id.Value.ToString()), default);

        Assert.True(result.IsSuccess);
        AdminFamilyListItem item = Assert.Single(result.Value.Items);
        Assert.Equal(family.Id.Value, item.FamilyId);
        Assert.Equal(family.OwnerUserId.Value, item.OwnerUserId);
        Assert.Equal(2, item.MemberCount);
        Assert.NotNull(item.DeletedOnUtc);

        string json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("SECRET_REASON", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_MESSAGE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deletionReason", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deletionMessage", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_publishes_members_and_dependents_without_any_personal_or_clinical_field()
    {
        await using TestFamilyDb db = await TestFamilyDb.CreateAsync();
        UserId owner = UserId.New();
        UserId invited = UserId.New();
        Family family = Family.Create(owner, "Detail Family");
        family.AddMember(FamilyMember.Create(invited, owner, FamilyRelationshipType.Mother, FamilyRole.Viewer));
        Elderly dependent = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("ELDERLY_ARABIC_SECRET"),
            FullName.Create("ELDERLY_ENGLISH_SECRET"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 1, 1),
            "SECRET_PROFILE_KEY",
            "SECRET_ADDRESS",
            "SECRET_HEALTH_NOTES");
        db.Context.Families.Add(family);
        db.Context.Elderlies.Add(dependent);
        await db.Context.SaveChangesAsync();

        var gateway = new StubFamilyIdentityGateway();
        gateway.Profiles[invited] = new FamilyMemberProfile(
            invited,
            "MEMBER_ARABIC_SECRET",
            "MEMBER_ENGLISH_SECRET",
            "member-secret@example.com",
            "01000000000",
            "SECRET_AVATAR",
            Status: 2,
            LastLoginOnUtc: new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));

        var result = await new GetAdminFamilyQueryHandler(db.Context, gateway)
            .Handle(new(family.Id.Value), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(family.Name, result.Value.Name);
        Assert.Equal(owner.Value, result.Value.OwnerUserId);
        Assert.Equal(2, result.Value.MemberCount);
        Assert.Null(result.Value.DeletedOnUtc);
        Assert.Equal(2, result.Value.Members.Count);

        AdminFamilyMemberItem ownerRow = result.Value.Members.Single(member => member.UserId == owner.Value);
        Assert.Equal(FamilyRole.Owner, ownerRow.Role);
        Assert.Equal(owner.Value, ownerRow.AddedByUserId);
        Assert.NotEqual(default, ownerRow.JoinedOnUtc);
        // No profile for the owner in this stub: the status is reported as 0
        // instead of a fabricated account state.
        Assert.Equal(0, ownerRow.Status);

        AdminFamilyMemberItem invitedRow = result.Value.Members.Single(member => member.UserId == invited.Value);
        Assert.Equal(FamilyRole.Viewer, invitedRow.Role);
        Assert.Equal(FamilyRelationshipType.Mother, invitedRow.RelationshipType);
        Assert.Equal(owner.Value, invitedRow.AddedByUserId);
        Assert.Equal(2, invitedRow.Status);

        AdminFamilyDependentItem dependentRow = Assert.Single(result.Value.Dependents);
        Assert.Equal(dependent.Id.Value, dependentRow.ElderlyId);
        Assert.Equal(dependent.IdentityUserId.Value, dependentRow.IdentityUserId);
        Assert.Equal(FamilyRelationshipType.Father, dependentRow.RelationshipType);

        string json = JsonSerializer.Serialize(result.Value);
        foreach (string secret in new[]
                 {
                     "ELDERLY_ARABIC_SECRET", "ELDERLY_ENGLISH_SECRET", "SECRET_PROFILE_KEY",
                     "SECRET_ADDRESS", "SECRET_HEALTH_NOTES", "MEMBER_ARABIC_SECRET",
                     "MEMBER_ENGLISH_SECRET", "member-secret@example.com", "01000000000", "SECRET_AVATAR"
                 })
        {
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        }

        foreach (string forbidden in new[]
                 {
                     "arabicFullName", "englishFullName", "email", "phone", "avatar", "healthNotes",
                     "address", "medicalProfile", "dateOfBirth", "gender", "note"
                 })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Detail_includes_dependents_of_a_deleted_family_and_reports_the_deletion_instant()
    {
        await using TestFamilyDb db = await TestFamilyDb.CreateAsync();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Deleted detail");
        Elderly dependent = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن"),
            FullName.Create("Elderly"),
            Gender.Male,
            new DateOnly(1948, 5, 5),
            new DateOnly(2026, 1, 1));
        db.Context.Families.Add(family);
        db.Context.Elderlies.Add(dependent);
        await db.Context.SaveChangesAsync();

        family.MarkDeleted("SECRET_REASON", null);
        await db.Context.SaveChangesAsync();

        var result = await new GetAdminFamilyQueryHandler(db.Context, new StubFamilyIdentityGateway())
            .Handle(new(family.Id.Value), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.DeletedOnUtc);
        Assert.Single(result.Value.Members);
        Assert.Equal(dependent.Id.Value, Assert.Single(result.Value.Dependents).ElderlyId);
        Assert.DoesNotContain("SECRET_REASON", JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_returns_not_found_for_an_unknown_or_empty_family_id()
    {
        await using TestFamilyDb db = await TestFamilyDb.CreateAsync();
        var handler = new GetAdminFamilyQueryHandler(db.Context, new StubFamilyIdentityGateway());

        var unknown = await handler.Handle(new(Guid.NewGuid()), default);
        var empty = await handler.Handle(new(Guid.Empty), default);

        Assert.False(unknown.IsSuccess);
        Assert.Equal("Families.Family.NotFound", unknown.Error.Code);
        Assert.False(empty.IsSuccess);
        Assert.Equal("Families.Family.NotFound", empty.Error.Code);
    }

    private sealed class StubFamilyIdentityGateway : IFamilyIdentityGateway
    {
        public Dictionary<UserId, FamilyMemberProfile> Profiles { get; } = [];

        public Task<IReadOnlyList<FamilyMemberProfile>> GetFamilyMemberProfilesAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FamilyMemberProfile>>(
                [.. Profiles.Values.Where(profile => userIds.Contains(profile.UserId))]);

        public Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(
            string phoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ElderlyIdentityAccount>.Failure(
                new Error("Test.NotImplemented", "Not implemented for this read.")));

        public Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(
            string arabicFullName,
            string englishFullName,
            string phoneNumber,
            Gender gender,
            DateOnly dateOfBirth,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ElderlyIdentityAccount>.Failure(
                new Error("Test.NotImplemented", "Not implemented for this read.")));

        public Task DeleteElderlyAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Result<FamilyInviteeAccount>> GetFamilyInviteeByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<FamilyInviteeAccount>.Failure(
                new Error("Test.NotImplemented", "Not implemented for this read.")));

        public Task SendFamilyInvitationEmailAsync(
            string email,
            string familyName,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Result> AnonymizeFamilyAccountsAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class TestFamilyDb : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestFamilyDb(SqliteConnection connection, FamiliesDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public FamiliesDbContext Context { get; }

        public static async Task<TestFamilyDb> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new FamiliesDbContext(
                new DbContextOptionsBuilder<FamiliesDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();
            return new TestFamilyDb(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
