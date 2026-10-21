using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

/// <summary>
/// E07 append-only caregiver review notes. Verifies the pinned text bounds
/// (required, trimmed, non-whitespace, at most 2000 characters stored verbatim),
/// that the author and the UTC instant come from the caller's actor and clock
/// and never from the note text, that an unknown or empty caregiver is the 404
/// code while a bad text is the 400 code (with the caregiver checked first), that
/// the read is bounded and deterministic — newest first by createdOnUtc, then by
/// note id descending, with the count covering the whole note set — that paging
/// is clamped rather than rejected, and that the type and the handlers expose no
/// update or delete path at all, so a second note never alters the first.
/// </summary>
public sealed class CaregiverReviewNoteTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Write_TrimsTheTextAndStoresItVerbatimOtherwise()
    {
        CaregiverId caregiverId = CaregiverId.New();
        UserId authorUserId = UserId.New();

        CaregiverReviewNote trimmed = CaregiverReviewNote.Write(
            caregiverId, authorUserId, "  Retained administrative note.  ", Now);
        CaregiverReviewNote innerWhitespace = CaregiverReviewNote.Write(
            caregiverId, authorUserId, "line one\n  line two\ttail ", Now.AddMinutes(1));

        Assert.NotEqual(Guid.Empty, trimmed.Id);
        Assert.NotEqual(trimmed.Id, innerWhitespace.Id);
        Assert.Equal(caregiverId, trimmed.CaregiverId);
        Assert.Equal(authorUserId, trimmed.AuthorUserId);
        Assert.Equal("Retained administrative note.", trimmed.Text);
        Assert.Equal("line one\n  line two\ttail", innerWhitespace.Text);
        Assert.Equal(Now, trimmed.CreatedOnUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public void Write_RejectsAMissingOrWhitespaceOnlyText(string? text)
    {
        Assert.Throws<DomainException>(() =>
            CaregiverReviewNote.Write(CaregiverId.New(), UserId.New(), text, Now));
    }

    [Fact]
    public void Write_RejectsAnOverLengthTextAndAcceptsTheExactBound()
    {
        CaregiverId caregiverId = CaregiverId.New();
        UserId authorUserId = UserId.New();
        string bound = new('x', CaregiverReviewNote.MaximumTextLength);
        string overLength = new('x', CaregiverReviewNote.MaximumTextLength + 1);

        CaregiverReviewNote accepted = CaregiverReviewNote.Write(
            caregiverId, authorUserId, bound, Now);

        Assert.Equal(bound, accepted.Text);
        Assert.Equal(CaregiverReviewNote.MaximumTextLength, accepted.Text.Length);

        // The bound applies to the trimmed text, so surrounding whitespace can
        // never push an accepted note past 2000 stored characters.
        Assert.Throws<DomainException>(() =>
            CaregiverReviewNote.Write(caregiverId, authorUserId, overLength, Now));
        Assert.Throws<DomainException>(() =>
            CaregiverReviewNote.Write(caregiverId, authorUserId, $"  {overLength}  ", Now));
    }

    [Fact]
    public void Write_RequiresACaregiverAnAuthorAndAUtcInstant()
    {
        Assert.Throws<DomainException>(() =>
            CaregiverReviewNote.Write(CaregiverId.Empty, UserId.New(), "note", Now));
        Assert.Throws<DomainException>(() =>
            CaregiverReviewNote.Write(CaregiverId.New(), UserId.Empty, "note", Now));
        Assert.Throws<DomainException>(() =>
            CaregiverReviewNote.Write(
                CaregiverId.New(),
                UserId.New(),
                "note",
                DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Write_IsTheOnlyPublicMemberSoNoUpdateOrDeletePathExists()
    {
        Assert.Equal(
            ["Write"],
            typeof(CaregiverReviewNote)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(method => method.Name));

        Assert.Empty(
            typeof(CaregiverReviewNote)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name));

        Assert.DoesNotContain(
            typeof(CaregiverReviewNote).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.SetMethod is { IsPublic: true });

        string[] mutatingTokens = ["update", "delete", "remove", "edit", "set", "append"];
        Assert.DoesNotContain(
            typeof(CaregiverReviewNote).GetMethods().Select(method => method.Name),
            name => mutatingTokens.Any(
                token => name.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void TheProjectionPublishesOnlyTheAdministrativeFields()
    {
        string[] published = typeof(AdminCaregiverReviewNoteItem)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            ["NoteId", "CaregiverId", "Text", "AuthorUserId", "CreatedOnUtc"],
            published);

        Assert.Equal(
            ["Items", "Page", "PageSize", "TotalCount"],
            typeof(AdminCaregiverReviewNotePage)
                .GetProperties()
                .Select(property => property.Name));

        string[] forbiddenTokens =
        [
            "phone", "email", "avatar", "identity", "document", "medical", "address",
            "national", "birth", "name"
        ];
        Assert.DoesNotContain(
            published,
            name => forbiddenTokens.Any(
                token => name.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void TheErrorsUseThePinnedPrefixAndCarryNoConflictCode()
    {
        Assert.Equal(
            "Caregivers.ReviewNote.CaregiverNotFound",
            CaregiverReviewNoteErrors.CaregiverNotFound.Code);
        Assert.Equal(
            "Caregivers.ReviewNote.InvalidText",
            CaregiverReviewNoteErrors.InvalidText.Code);

        // Nothing in this phase is concurrent or mutable, so no 409 code exists.
        Assert.DoesNotContain(
            typeof(CaregiverReviewNoteErrors)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.Name),
            name => name.Contains("Conflict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Add_ThenList_RoundTripsTheTrimmedTextTheActorAndTheClock()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);
        UserId authorUserId = UserId.New();

        Result<AdminCaregiverReviewNoteItem> added = await AddAsync(
            db, caregiver.Id, authorUserId, "   Round trip administrative note.   ", Now);

        Assert.True(added.IsSuccess);
        Assert.NotEqual(Guid.Empty, added.Value.NoteId);
        Assert.Equal(caregiver.Id.Value, added.Value.CaregiverId);
        Assert.Equal("Round trip administrative note.", added.Value.Text);
        Assert.Equal(authorUserId.Value, added.Value.AuthorUserId);
        Assert.Equal(Now, added.Value.CreatedOnUtc);

        CaregiverReviewNote stored = await db.ReviewNotes
            .SingleAsync(note => note.Id == added.Value.NoteId);
        Assert.Equal(caregiver.Id, stored.CaregiverId);
        Assert.Equal(authorUserId, stored.AuthorUserId);
        Assert.Equal("Round trip administrative note.", stored.Text);
        Assert.Equal(Now, stored.CreatedOnUtc);

        Result<AdminCaregiverReviewNotePage> listed = await ListAsync(db, caregiver.Id, 1, 20);

        Assert.True(listed.IsSuccess);
        Assert.Equal(1, listed.Value.Page);
        Assert.Equal(20, listed.Value.PageSize);
        Assert.Equal(1, listed.Value.TotalCount);
        Assert.Equal(added.Value, Assert.Single(listed.Value.Items));
    }

    [Fact]
    public async Task Add_InvalidText_IsRejectedAndWritesNothing()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);
        UserId authorUserId = UserId.New();

        foreach (string? text in new string?[]
                 {
                     null,
                     string.Empty,
                     "   ",
                     "\t",
                     new string('x', CaregiverReviewNote.MaximumTextLength + 1)
                 })
        {
            Result<AdminCaregiverReviewNoteItem> result = await AddAsync(
                db, caregiver.Id, authorUserId, text, Now);

            Assert.False(result.IsSuccess);
            Assert.Equal(CaregiverReviewNoteErrors.InvalidText, result.Error);
            Assert.Equal("Caregivers.ReviewNote.InvalidText", result.Error.Code);
        }

        Assert.Empty(await db.ReviewNotes.ToListAsync());

        Result<AdminCaregiverReviewNotePage> listed = await ListAsync(db, caregiver.Id, 1, 20);
        Assert.True(listed.IsSuccess);
        Assert.Empty(listed.Value.Items);
        Assert.Equal(0, listed.Value.TotalCount);
    }

    [Fact]
    public async Task Add_ForAnUnknownOrEmptyCaregiver_ReturnsNotFoundAndWritesNothing()
    {
        using CaregiversDbContext db = CreateDb();
        await AddCaregiverAsync(db);

        Result<AdminCaregiverReviewNoteItem> missing = await AddAsync(
            db, CaregiverId.New(), UserId.New(), "note", Now);
        Result<AdminCaregiverReviewNoteItem> empty = await AddAsync(
            db, CaregiverId.Empty, UserId.New(), "note", Now);

        Assert.False(missing.IsSuccess);
        Assert.Equal(CaregiverReviewNoteErrors.CaregiverNotFound, missing.Error);
        Assert.Equal("Caregivers.ReviewNote.CaregiverNotFound", missing.Error.Code);
        Assert.False(empty.IsSuccess);
        Assert.Equal(CaregiverReviewNoteErrors.CaregiverNotFound, empty.Error);
        Assert.Empty(await db.ReviewNotes.ToListAsync());
    }

    [Fact]
    public async Task Add_UnknownCaregiverWithAnInvalidText_ReportsTheCaregiverFirst()
    {
        using CaregiversDbContext db = CreateDb();

        Result<AdminCaregiverReviewNoteItem> result = await AddAsync(
            db, CaregiverId.New(), UserId.New(), "   ", Now);

        Assert.False(result.IsSuccess);
        Assert.Equal(CaregiverReviewNoteErrors.CaregiverNotFound, result.Error);
        Assert.Empty(await db.ReviewNotes.ToListAsync());
    }

    [Fact]
    public async Task List_UnknownOrEmptyCaregiver_ReturnsTheNotFoundCode()
    {
        using CaregiversDbContext db = CreateDb();

        Result<AdminCaregiverReviewNotePage> missing = await ListAsync(db, CaregiverId.New(), 1, 20);
        Result<AdminCaregiverReviewNotePage> empty = await ListAsync(db, CaregiverId.Empty, 1, 20);

        Assert.False(missing.IsSuccess);
        Assert.Equal(CaregiverReviewNoteErrors.CaregiverNotFound, missing.Error);
        Assert.False(empty.IsSuccess);
        Assert.Equal(CaregiverReviewNoteErrors.CaregiverNotFound, empty.Error);
    }

    [Fact]
    public async Task List_ACaregiverWithNoNotes_ReturnsAnEmptyPageAndNeverAnError()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);

        Result<AdminCaregiverReviewNotePage> listed = await ListAsync(db, caregiver.Id, 1, 20);

        Assert.True(listed.IsSuccess);
        Assert.Empty(listed.Value.Items);
        Assert.Equal(0, listed.Value.TotalCount);
        Assert.Equal(1, listed.Value.Page);
        Assert.Equal(20, listed.Value.PageSize);
    }

    [Fact]
    public async Task Add_IsAppendOnly_ASecondNoteNeverAltersTheFirst()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);
        UserId firstAuthor = UserId.New();

        Result<AdminCaregiverReviewNoteItem> first = await AddAsync(
            db, caregiver.Id, firstAuthor, "Retained note.", Now);
        Assert.True(first.IsSuccess);

        Result<AdminCaregiverReviewNoteItem> second = await AddAsync(
            db, caregiver.Id, UserId.New(), "Retained note.", Now.AddMinutes(5));
        Assert.True(second.IsSuccess);

        // The same text twice is two distinct retained rows, never one row
        // rewritten: the first note keeps its own id, author and instant.
        Assert.NotEqual(first.Value.NoteId, second.Value.NoteId);
        Assert.Equal(2, await db.ReviewNotes.CountAsync());

        CaregiverReviewNote reread = await db.ReviewNotes
            .AsNoTracking()
            .SingleAsync(note => note.Id == first.Value.NoteId);
        Assert.Equal(caregiver.Id, reread.CaregiverId);
        Assert.Equal(firstAuthor, reread.AuthorUserId);
        Assert.Equal("Retained note.", reread.Text);
        Assert.Equal(Now, reread.CreatedOnUtc);
        Assert.Equal("Retained note.", second.Value.Text);
        Assert.Equal(Now.AddMinutes(5), second.Value.CreatedOnUtc);

        // Appending only ever adds: every tracked note is unmodified, so no
        // handler path updates or deletes a retained row.
        Assert.Equal(2, db.ChangeTracker.Entries<CaregiverReviewNote>().Count());
        Assert.All(
            db.ChangeTracker.Entries<CaregiverReviewNote>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public async Task List_OrdersNewestFirstWithTheNoteIdTiebreak()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);
        Caregiver otherCaregiver = await AddCaregiverAsync(db);
        UserId authorUserId = UserId.New();
        DateTime sharedInstant = Now.AddDays(-2);

        CaregiverReviewNote oldest = AddNote(
            db, caregiver.Id, authorUserId, "oldest retained note", Now.AddDays(-3));
        CaregiverReviewNote tiedLowId = AddNote(
            db, caregiver.Id, authorUserId, "tied note with the lower id", sharedInstant);
        CaregiverReviewNote tiedHighId = AddNote(
            db, caregiver.Id, authorUserId, "tied note with the higher id", sharedInstant);
        CaregiverReviewNote newest = AddNote(db, caregiver.Id, authorUserId, "newest retained note", Now);
        CaregiverReviewNote foreign = AddNote(
            db, otherCaregiver.Id, authorUserId, "another caregiver's note", Now.AddDays(10));

        // The tied pair is pinned to ids whose high/low order is unambiguous in
        // both .NET and PostgreSQL uuid ordering.
        SetNoteId(db, tiedLowId, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        SetNoteId(db, tiedHighId, Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await db.SaveChangesAsync();

        Result<AdminCaregiverReviewNotePage> listed = await ListAsync(db, caregiver.Id, 1, 100);

        Assert.True(listed.IsSuccess);
        Assert.Equal(4, listed.Value.TotalCount);
        Assert.Equal(
            [newest.Id, tiedHighId.Id, tiedLowId.Id, oldest.Id],
            listed.Value.Items.Select(item => item.NoteId));
        Assert.DoesNotContain(
            listed.Value.Items.Select(item => item.NoteId),
            noteId => noteId == foreign.Id);

        Result<AdminCaregiverReviewNotePage> otherListed = await ListAsync(db, otherCaregiver.Id, 1, 100);
        Assert.True(otherListed.IsSuccess);
        Assert.Equal(1, otherListed.Value.TotalCount);
        Assert.Equal(foreign.Id, Assert.Single(otherListed.Value.Items).NoteId);
    }

    [Fact]
    public async Task List_PagesTheWholeNoteSetAndKeepsTheFullCount()
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);
        UserId authorUserId = UserId.New();

        CaregiverReviewNote oldestNote = AddNote(db, caregiver.Id, authorUserId, "note one", Now);
        CaregiverReviewNote secondOldest = AddNote(db, caregiver.Id, authorUserId, "note two", Now.AddMinutes(1));
        CaregiverReviewNote secondNewest = AddNote(db, caregiver.Id, authorUserId, "note three", Now.AddMinutes(2));
        CaregiverReviewNote newestNote = AddNote(db, caregiver.Id, authorUserId, "note four", Now.AddMinutes(3));
        await db.SaveChangesAsync();

        Result<AdminCaregiverReviewNotePage> firstPage = await ListAsync(db, caregiver.Id, 1, 2);
        Result<AdminCaregiverReviewNotePage> secondPage = await ListAsync(db, caregiver.Id, 2, 2);
        Result<AdminCaregiverReviewNotePage> beyondPage = await ListAsync(db, caregiver.Id, 9, 2);

        Assert.Equal([newestNote.Id, secondNewest.Id], firstPage.Value.Items.Select(item => item.NoteId));
        Assert.Equal([secondOldest.Id, oldestNote.Id], secondPage.Value.Items.Select(item => item.NoteId));
        Assert.Empty(beyondPage.Value.Items);
        Assert.Equal(2, firstPage.Value.PageSize);
        Assert.Equal(1, firstPage.Value.Page);
        Assert.Equal(2, secondPage.Value.Page);
        Assert.Equal(9, beyondPage.Value.Page);
        Assert.All(
            new[] { firstPage, secondPage, beyondPage },
            page =>
            {
                Assert.True(page.IsSuccess);
                Assert.Equal(4, page.Value.TotalCount);
            });

        // The window is a partition: no page repeats a retained note.
        Assert.DoesNotContain(
            secondPage.Value.Items.Select(item => item.NoteId),
            noteId => firstPage.Value.Items.Any(item => item.NoteId == noteId));
    }

    [Theory]
    [InlineData(0, 0, 1, 20)]
    [InlineData(-4, -7, 1, 20)]
    [InlineData(1, 0, 1, 20)]
    [InlineData(0, 101, 1, 100)]
    [InlineData(0, 100, 1, 100)]
    [InlineData(-1, 1, 1, 1)]
    public async Task List_ClampsPagingInsteadOfRejectingIt(
        int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        using CaregiversDbContext db = CreateDb();
        Caregiver caregiver = await AddCaregiverAsync(db);

        Result<AdminCaregiverReviewNotePage> listed = await ListAsync(db, caregiver.Id, page, pageSize);

        Assert.True(listed.IsSuccess);
        Assert.Equal(expectedPage, listed.Value.Page);
        Assert.Equal(expectedPageSize, listed.Value.PageSize);
    }

    private static CaregiversDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Caregiver> AddCaregiverAsync(CaregiversDbContext db)
    {
        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Companion);
        db.Caregivers.Add(caregiver);
        await db.SaveChangesAsync();
        return caregiver;
    }

    private static CaregiverReviewNote AddNote(
        CaregiversDbContext db,
        CaregiverId caregiverId,
        UserId authorUserId,
        string text,
        DateTime createdOnUtc)
    {
        CaregiverReviewNote note = CaregiverReviewNote.Write(
            caregiverId, authorUserId, text, createdOnUtc);
        db.ReviewNotes.Add(note);
        return note;
    }

    private static void SetNoteId(CaregiversDbContext db, CaregiverReviewNote note, Guid id) =>
        db.Entry(note).Property(entity => entity.Id).CurrentValue = id;

    private static Task<Result<AdminCaregiverReviewNoteItem>> AddAsync(
        ICaregiversDbContext db,
        CaregiverId caregiverId,
        UserId authorUserId,
        string? text,
        DateTime utcNow) =>
        new AddAdminCaregiverReviewNoteCommandHandler(db)
            .Handle(
                new AddAdminCaregiverReviewNoteCommand(caregiverId, authorUserId, text, utcNow),
                default);

    private static Task<Result<AdminCaregiverReviewNotePage>> ListAsync(
        ICaregiversDbContext db,
        CaregiverId caregiverId,
        int page,
        int pageSize) =>
        new ListAdminCaregiverReviewNotesQueryHandler(db)
            .Handle(new ListAdminCaregiverReviewNotesQuery(caregiverId, page, pageSize), default);
}
