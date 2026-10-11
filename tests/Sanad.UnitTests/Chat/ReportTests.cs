using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Application.Reports;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Reports;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Chat Report rules. The first half pins the domain aggregate: reason length
/// boundary, optional message target, and the single-shot Open → Dismissed /
/// Open → Upheld lifecycle with actor/time audit — both transitions one-way,
/// a resolved report never moves again. The second half drives the Lane B
/// report handlers and controllers of frozen endpoints 11–15 over an EF
/// InMemory ChatDbContext: submit with its privacy gates, the support queue
/// and read-only reported-conversation view, dismiss/uphold with the mutual
/// block on uphold, and the ChatSupportAdmin wiring.
/// </summary>
public sealed class ReportTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 8, 15, 0, 0), DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Create — reason boundaries.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_AcceptsReasonOfExactlyFiveHundredCharacters()
    {
        string reason = new('r', Report.MaximumReasonLength);

        Report report = CreateReport(reason);

        Assert.Equal(Report.MaximumReasonLength, report.Reason.Length);
        Assert.Equal(ReportStatus.Open, report.Status);
    }

    [Fact]
    public void Create_RejectsReasonOverFiveHundredCharacters()
    {
        string reason = new('r', Report.MaximumReasonLength + 1);

        Assert.Throws<DomainException>(() => CreateReport(reason));
    }

    [Fact]
    public void Create_RejectsEmptyReasonAndTrims()
    {
        Assert.Throws<DomainException>(() => CreateReport(null));
        Assert.Throws<DomainException>(() => CreateReport(string.Empty));
        Assert.Throws<DomainException>(() => CreateReport("   "));

        Report report = CreateReport("  spam  ");
        Assert.Equal("spam", report.Reason);
    }

    // ------------------------------------------------------------------
    // Create — optional message target and required references.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_MessageReferenceIsOptional()
    {
        Report conversationLevel = CreateReport("abuse", messageId: null);
        MessageId messageId = MessageId.New();
        Report messageLevel = CreateReport("abuse", messageId);

        Assert.Null(conversationLevel.MessageId);
        Assert.Equal(messageId, messageLevel.MessageId);
    }

    [Fact]
    public void Create_RejectsMissingConversationOrReporterOrNonUtcStamp()
    {
        UserId reporter = UserId.New();

        Assert.Throws<DomainException>(() => Report.Create(
            ConversationId.Empty, null, reporter, "spam", UtcStamp));
        Assert.Throws<DomainException>(() => Report.Create(
            ConversationId.New(), null, UserId.Empty, "spam", UtcStamp));
        Assert.Throws<DomainException>(() => Report.Create(
            ConversationId.New(), null, reporter, "spam", DateTime.Now));
        Assert.Throws<DomainException>(() => Report.Create(
            ConversationId.New(), null, reporter, "spam",
            new DateTime(2026, 10, 8, 15, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Create_LeavesResolutionFieldsNullAndStampUtc()
    {
        Report report = CreateReport("spam");

        Assert.Equal(UtcStamp, report.CreatedOnUtc);
        Assert.Equal(DateTimeKind.Utc, report.CreatedOnUtc.Kind);
        Assert.Null(report.ResolvedOnUtc);
        Assert.Null(report.ResolvedBy);
    }

    // ------------------------------------------------------------------
    // Lifecycle — one-way transitions with audit.
    // ------------------------------------------------------------------

    [Fact]
    public void Dismiss_ResolvesAnOpenReportAndRecordsTheAudit()
    {
        Report report = CreateReport("not spam after review");
        UserId moderator = UserId.New();
        DateTime resolvedAt = UtcStamp.AddHours(2);

        report.Dismiss(moderator, resolvedAt);

        Assert.Equal(ReportStatus.Dismissed, report.Status);
        Assert.Equal(resolvedAt, report.ResolvedOnUtc);
        Assert.Equal(DateTimeKind.Utc, report.ResolvedOnUtc!.Value.Kind);
        Assert.Equal(moderator, report.ResolvedBy);

        // Dismissal retains the full history — nothing is erased.
        Assert.Equal("not spam after review", report.Reason);
        Assert.Equal(UtcStamp, report.CreatedOnUtc);
    }

    [Fact]
    public void Uphold_ResolvesAnOpenReportAndRecordsTheAudit()
    {
        Report report = CreateReport("harassment");
        UserId moderator = UserId.New();
        DateTime resolvedAt = UtcStamp.AddHours(3);

        report.Uphold(moderator, resolvedAt);

        Assert.Equal(ReportStatus.Upheld, report.Status);
        Assert.Equal(resolvedAt, report.ResolvedOnUtc);
        Assert.Equal(moderator, report.ResolvedBy);
    }

    [Fact]
    public void Resolve_RejectsDoubleResolutionAndKeepsTheFirstOutcome()
    {
        Report dismissed = CreateReport("spam");
        dismissed.Dismiss(UserId.New(), UtcStamp.AddHours(1));
        Assert.Throws<DomainException>(() =>
            dismissed.Dismiss(UserId.New(), UtcStamp.AddHours(2)));

        Report upheld = CreateReport("harassment");
        upheld.Uphold(UserId.New(), UtcStamp.AddHours(1));
        Assert.Throws<DomainException>(() =>
            upheld.Uphold(UserId.New(), UtcStamp.AddHours(2)));

        // The first resolution's audit survives the rejected attempt.
        Assert.Equal(ReportStatus.Dismissed, dismissed.Status);
        Assert.Equal(ReportStatus.Upheld, upheld.Status);
    }

    [Fact]
    public void Resolve_NeverMovesBetweenTerminalStates()
    {
        Report dismissed = CreateReport("spam");
        dismissed.Dismiss(UserId.New(), UtcStamp.AddHours(1));
        Assert.Throws<DomainException>(() =>
            dismissed.Uphold(UserId.New(), UtcStamp.AddHours(2)));

        Report upheld = CreateReport("harassment");
        upheld.Uphold(UserId.New(), UtcStamp.AddHours(1));
        Assert.Throws<DomainException>(() =>
            upheld.Dismiss(UserId.New(), UtcStamp.AddHours(2)));

        Assert.Equal(ReportStatus.Dismissed, dismissed.Status);
        Assert.Equal(ReportStatus.Upheld, upheld.Status);
    }

    [Fact]
    public void Resolve_RejectsEmptyActorAndNonUtcTime()
    {
        Report report = CreateReport("spam");

        Assert.Throws<DomainException>(() =>
            report.Dismiss(UserId.Empty, UtcStamp.AddHours(1)));
        Assert.Throws<DomainException>(() =>
            report.Uphold(UserId.New(), DateTime.Now));
        Assert.Throws<DomainException>(() =>
            report.Uphold(
                UserId.New(),
                new DateTime(2026, 10, 8, 17, 0, 0, DateTimeKind.Unspecified)));

        // A rejected resolution leaves the report open.
        Assert.Equal(ReportStatus.Open, report.Status);
        Assert.Null(report.ResolvedOnUtc);
        Assert.Null(report.ResolvedBy);
    }

    // ------------------------------------------------------------------
    // Endpoint 11 — submit.
    // ------------------------------------------------------------------

    [Fact]
    public async Task SubmitReport_CreatesOpenReportsAndReturnsTheirIds()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);
        Message message = SeedMessage(
            db, conversation, alice, UtcStamp.AddMinutes(1));
        await db.SaveChangesAsync();

        var handler = new SubmitReportCommandHandler(db);

        Result<SubmitReportResponse> conversationLevel = await handler.Handle(
            new SubmitReportCommand(
                alice, conversation.Id, null, "  spam  "),
            default);
        Result<SubmitReportResponse> messageLevel = await handler.Handle(
            new SubmitReportCommand(
                bob, conversation.Id, message.Id, "abuse"),
            default);

        Assert.True(conversationLevel.IsSuccess);
        Assert.True(messageLevel.IsSuccess);
        Assert.NotEqual(
            conversationLevel.Value.ReportId,
            messageLevel.Value.ReportId);
        Assert.NotEqual(Guid.Empty, conversationLevel.Value.ReportId);

        List<Report> rows = await db.Reports.ToListAsync();

        Report reported = rows.Single(row =>
            row.Id.Value == conversationLevel.Value.ReportId);
        Assert.Equal(ReportStatus.Open, reported.Status);
        Assert.Equal(alice, reported.ReporterUserId);
        Assert.Equal(conversation.Id, reported.ConversationId);
        Assert.Null(reported.MessageId);
        Assert.Equal("spam", reported.Reason);
        Assert.Null(reported.ResolvedBy);
        Assert.Null(reported.ResolvedOnUtc);

        Report targeted = rows.Single(row =>
            row.Id.Value == messageLevel.Value.ReportId);
        Assert.Equal(ReportStatus.Open, targeted.Status);
        Assert.Equal(bob, targeted.ReporterUserId);
        Assert.Equal(message.Id, targeted.MessageId);
        Assert.Equal("abuse", targeted.Reason);
    }

    [Fact]
    public async Task SubmitReport_UnknownForeignAndCrossConversationTargetsShareThePrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);

        UserId carol = UserId.New();
        UserId dave = UserId.New();
        Conversation foreign = CreateDirect(carol, dave, UtcStamp);
        db.Conversations.Add(foreign);
        Message elsewhere = Message.Create(
            foreign.Id,
            carol,
            MessageType.Text,
            "elsewhere",
            null,
            "client-elsewhere",
            UtcStamp);
        db.Messages.Add(elsewhere);
        await db.SaveChangesAsync();

        var handler = new SubmitReportCommandHandler(db);

        // Not a participant, unknown conversation, unknown messageId, and a
        // message of another conversation are one indistinguishable 404 —
        // this route can never be used to probe ids or memberships.
        Result<SubmitReportResponse> notAParticipant = await handler.Handle(
            new SubmitReportCommand(
                alice, foreign.Id, null, "spam"),
            default);
        Result<SubmitReportResponse> unknownConversation = await handler.Handle(
            new SubmitReportCommand(
                alice, ConversationId.New(), null, "spam"),
            default);
        Result<SubmitReportResponse> unknownMessage = await handler.Handle(
            new SubmitReportCommand(
                alice, conversation.Id, MessageId.New(), "spam"),
            default);
        Result<SubmitReportResponse> crossConversationMessage = await handler.Handle(
            new SubmitReportCommand(
                alice, conversation.Id, elsewhere.Id, "spam"),
            default);

        Assert.All(
            new[]
            {
                notAParticipant,
                unknownConversation,
                unknownMessage,
                crossConversationMessage
            },
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(
                    ReportErrors.NotFound.Code,
                    result.Error.Code);
            });

        Assert.Empty(db.Reports);
    }

    [Fact]
    public async Task SubmitReport_RejectsReasonOutsideTheFrozenBounds()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);

        var handler = new SubmitReportCommandHandler(db);

        Result<SubmitReportResponse> missing = await handler.Handle(
            new SubmitReportCommand(alice, conversation.Id, null, null),
            default);
        Result<SubmitReportResponse> empty = await handler.Handle(
            new SubmitReportCommand(alice, conversation.Id, null, string.Empty),
            default);
        Result<SubmitReportResponse> blank = await handler.Handle(
            new SubmitReportCommand(alice, conversation.Id, null, "   "),
            default);
        Result<SubmitReportResponse> oversized = await handler.Handle(
            new SubmitReportCommand(
                alice,
                conversation.Id,
                null,
                new string('r', Report.MaximumReasonLength + 1)),
            default);

        Assert.All(
            new[] { missing, empty, blank, oversized },
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(ReportErrors.Invalid.Code, result.Error.Code);
            });

        Result<SubmitReportResponse> boundary = await handler.Handle(
            new SubmitReportCommand(
                alice,
                conversation.Id,
                null,
                new string('r', Report.MaximumReasonLength)),
            default);

        Assert.True(boundary.IsSuccess);
        Report stored = Assert.Single(await db.Reports.ToListAsync());
        Assert.Equal(Report.MaximumReasonLength, stored.Reason.Length);
    }

    [Fact]
    public async Task SubmitReport_RequiresAJwtBuildsTheCommandAndAnswers201()
    {
        Guid reportId = Guid.NewGuid();
        var sender = new CapturingSender(
            Result<SubmitReportResponse>.Success(
                new SubmitReportResponse(reportId)));

        Assert.IsType<UnauthorizedResult>(
            await CreateController(sender).SubmitReport(
                new ChatSubmitReportRequest(Guid.NewGuid(), null, "spam"),
                default));
        Assert.Null(sender.LastRequest);

        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);
        Guid conversationId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();

        IActionResult created = await controller.SubmitReport(
            new ChatSubmitReportRequest(conversationId, messageId, "spam"),
            default);

        ObjectResult createdObject = Assert.IsType<ObjectResult>(created);
        Assert.Equal(StatusCodes.Status201Created, createdObject.StatusCode);
        Assert.Equal(
            reportId,
            Assert.IsType<SubmitReportResponse>(createdObject.Value).ReportId);

        SubmitReportCommand command =
            Assert.IsType<SubmitReportCommand>(sender.LastRequest);
        Assert.Equal(actor, command.Reporter);
        Assert.Equal(new ConversationId(conversationId), command.ConversationId);
        Assert.Equal(new MessageId(messageId), command.MessageId);
        Assert.Equal("spam", command.Reason);
    }

    [Fact]
    public async Task SubmitReport_ForcesThePrivacy404ToTheGenericBodyAndKeepsInvalidAt400()
    {
        UserId actor = UserId.New();
        Guid conversationId = Guid.NewGuid();

        ChatController unknown = CreateController(
            new CapturingSender(
                Result<SubmitReportResponse>.Failure(ReportErrors.NotFound)),
            actor);

        IActionResult notFound = await unknown.SubmitReport(
            new ChatSubmitReportRequest(conversationId, null, "spam"),
            default);

        ObjectResult notFoundObject = Assert.IsType<ObjectResult>(notFound);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundObject.StatusCode);
        var notFoundDetails = Assert.IsType<ProblemDetails>(notFoundObject.Value);
        Assert.Equal(
            ReportErrors.NotFound.Code,
            notFoundDetails.Extensions["code"]);
        // Generic detail: the caller learns nothing about the id.
        Assert.Equal(
            "The requested resource was not found.",
            notFoundDetails.Detail);

        ChatController invalid = CreateController(
            new CapturingSender(
                Result<SubmitReportResponse>.Failure(ReportErrors.Invalid)),
            actor);

        IActionResult badRequest = await invalid.SubmitReport(
            new ChatSubmitReportRequest(conversationId, null, string.Empty),
            default);

        ObjectResult badRequestObject = Assert.IsType<ObjectResult>(badRequest);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestObject.StatusCode);
        var invalidDetails = Assert.IsType<ProblemDetails>(badRequestObject.Value);
        Assert.Equal(ReportErrors.Invalid.Code, invalidDetails.Extensions["code"]);
    }

    // ------------------------------------------------------------------
    // Endpoint 12 — support queue.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetReports_FiltersByStatusPagesAndReturnsNewestFirst()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);
        UserId admin = UserId.New();

        Report first = SeedReport(
            db, conversation, alice, "one", UtcStamp.AddMinutes(1));
        Report second = SeedReport(
            db, conversation, alice, "two", UtcStamp.AddMinutes(2));
        Report third = SeedReport(
            db, conversation, bob, "three", UtcStamp.AddMinutes(3));
        Report fourth = SeedReport(
            db, conversation, bob, "four", UtcStamp.AddMinutes(4));

        second.Dismiss(admin, UtcStamp.AddHours(1));
        third.Uphold(admin, UtcStamp.AddHours(1));
        await db.SaveChangesAsync();

        var handler = new GetReportsQueryHandler(db);

        Result<PagedResult<ReportResponse>> pageOne = await handler.Handle(
            new GetReportsQuery(null, 1, 2), default);
        Result<PagedResult<ReportResponse>> pageTwo = await handler.Handle(
            new GetReportsQuery(null, 2, 2), default);

        // Newest report first, paged with the shared envelope.
        Assert.Equal(4, pageOne.Value.TotalCount);
        Assert.Equal(2, pageOne.Value.TotalPages);
        Assert.Equal(1, pageOne.Value.Page);
        Assert.Equal(
            new[] { fourth.Id.Value, third.Id.Value },
            pageOne.Value.Items.Select(item => item.ReportId));
        Assert.Equal(
            new[] { second.Id.Value, first.Id.Value },
            pageTwo.Value.Items.Select(item => item.ReportId));

        Result<PagedResult<ReportResponse>> open = await handler.Handle(
            new GetReportsQuery("open", 1, 20), default);
        Result<PagedResult<ReportResponse>> dismissed = await handler.Handle(
            new GetReportsQuery("DISMISSED", 1, 20), default);
        Result<PagedResult<ReportResponse>> upheld = await handler.Handle(
            new GetReportsQuery("Upheld", 1, 20), default);

        Assert.Equal(
            new[] { fourth.Id.Value, first.Id.Value },
            open.Value.Items.Select(item => item.ReportId));
        Assert.All(
            open.Value.Items,
            item => Assert.Equal("Open", item.Status));

        ReportResponse dismissedRow = Assert.Single(dismissed.Value.Items);
        Assert.Equal(second.Id.Value, dismissedRow.ReportId);
        Assert.Equal("Dismissed", dismissedRow.Status);
        Assert.Equal(admin.Value, dismissedRow.ResolvedBy);
        Assert.NotNull(dismissedRow.ResolvedOnUtc);

        ReportResponse upheldRow = Assert.Single(upheld.Value.Items);
        Assert.Equal(third.Id.Value, upheldRow.ReportId);
        Assert.Equal("Upheld", upheldRow.Status);

        // The retained history the queue shows.
        Assert.Equal("two", dismissedRow.Reason);
        Assert.Equal(alice.Value, dismissedRow.ReporterUserId);
        Assert.Equal(second.CreatedOnUtc, dismissedRow.CreatedOnUtc);
    }

    [Fact]
    public async Task GetReports_AnUnrecognisedStatusFilterIsTheCallerInvalid400()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);
        SeedReport(db, conversation, alice, "one", UtcStamp);
        await db.SaveChangesAsync();

        var handler = new GetReportsQueryHandler(db);

        Result<PagedResult<ReportResponse>> bogus = await handler.Handle(
            new GetReportsQuery("bogus", 1, 20), default);

        Assert.True(bogus.IsFailure);
        Assert.Equal(ReportErrors.Invalid.Code, bogus.Error.Code);

        // Blank is "no filter", not a typo.
        Result<PagedResult<ReportResponse>> blank = await handler.Handle(
            new GetReportsQuery("  ", 1, 20), default);

        Assert.True(blank.IsSuccess);
        Assert.Equal(1, blank.Value.TotalCount);
    }

    // ------------------------------------------------------------------
    // Endpoint 13 — detail and reported-conversation view.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetReport_ReturnsTheDetailWithItsAuditAndAnUnknownIdIsNotFound()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);
        UserId admin = UserId.New();

        Report report = SeedReport(
            db, conversation, alice, "harassment", UtcStamp);
        DateTime resolvedAt = UtcStamp.AddHours(2);
        report.Dismiss(admin, resolvedAt);
        await db.SaveChangesAsync();

        var handler = new GetReportQueryHandler(db);

        Result<ReportResponse> detail = await handler.Handle(
            new GetReportQuery(report.Id), default);

        Assert.True(detail.IsSuccess);
        Assert.Equal(report.Id.Value, detail.Value.ReportId);
        Assert.Equal(conversation.Id.Value, detail.Value.ConversationId);
        Assert.Null(detail.Value.MessageId);
        Assert.Equal(alice.Value, detail.Value.ReporterUserId);
        Assert.Equal("harassment", detail.Value.Reason);
        Assert.Equal("Dismissed", detail.Value.Status);
        Assert.Equal(UtcStamp, detail.Value.CreatedOnUtc);
        Assert.Equal(resolvedAt, detail.Value.ResolvedOnUtc);
        Assert.Equal(admin.Value, detail.Value.ResolvedBy);

        // Support is never a participant: no participation gate exists on
        // this path — only the report id, and an unknown one is a 404.
        Result<ReportResponse> unknown = await handler.Handle(
            new GetReportQuery(ReportId.New()), default);

        Assert.True(unknown.IsFailure);
        Assert.Equal(ReportErrors.NotFound.Code, unknown.Error.Code);
    }

    [Fact]
    public async Task GetReportMessages_ReturnsTheReportedConversationReadOnlyAndWithoutParticipation()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        Message older = SeedMessage(
            db, conversation, alice, UtcStamp.AddMinutes(1), "first");
        Message newer = SeedMessage(
            db, conversation, bob, UtcStamp.AddMinutes(2), "second");
        DateTime bobReadAt = UtcStamp.AddMinutes(10);
        db.ReadStates.Add(ConversationReadState.Create(
            conversation.Id, bob, newer.Id.Value, bobReadAt));

        Report report = SeedReport(
            db, conversation, alice, "harassment", UtcStamp);
        await db.SaveChangesAsync();

        var handler = new GetReportMessagesQueryHandler(db);

        Result<IReadOnlyList<MessageItemResponse>> view = await handler.Handle(
            new GetReportMessagesQuery(report.Id), default);

        Assert.True(view.IsSuccess);

        // Same item shape and ordering as endpoint 3.
        Assert.Equal(
            new[] { newer.Id.Value, older.Id.Value },
            view.Value.Select(item => item.Id));
        MessageItemResponse incoming = view.Value[0];
        Assert.Equal(newer.ClientMessageId, incoming.ClientMessageId);
        Assert.Equal(bob.Value, incoming.SenderUserId);
        Assert.Equal("Text", incoming.Type);
        Assert.Equal("second", incoming.Text);
        Assert.Null(incoming.Attachment);
        Assert.Equal(newer.SentOnUtc, incoming.SentOnUtc);

        // The read tick uses endpoint 3's rule with the reader fixed to the
        // non-sender: Bob's mark covers Alice's older message, and nothing
        // covers Bob's own.
        Assert.Equal(bobReadAt, view.Value[1].ReadOnUtc);
        Assert.Null(view.Value[0].ReadOnUtc);

        // Read-only: Support never stamps delivery, unlike endpoint 3.
        Assert.Null(db.Messages.Single(m => m.Id == older.Id).DeliveredOnUtc);
        Assert.Null(db.Messages.Single(m => m.Id == newer.Id).DeliveredOnUtc);

        Result<IReadOnlyList<MessageItemResponse>> unknown = await handler.Handle(
            new GetReportMessagesQuery(ReportId.New()), default);

        Assert.True(unknown.IsFailure);
        Assert.Equal(ReportErrors.NotFound.Code, unknown.Error.Code);
    }

    // ------------------------------------------------------------------
    // Endpoint 14 — dismiss.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dismiss_ClosesTheReportRetainsHistoryAndRejectsASecondResolve()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);
        Report report = SeedReport(
            db, conversation, alice, "not spam after review", UtcStamp);
        await db.SaveChangesAsync();

        UserId admin = UserId.New();
        var handler = new DismissReportCommandHandler(db);

        Assert.True((await handler.Handle(
            new DismissReportCommand(admin, report.Id), default)).IsSuccess);

        Assert.Equal(ReportStatus.Dismissed, report.Status);
        Assert.Equal(admin, report.ResolvedBy);
        Assert.NotNull(report.ResolvedOnUtc);
        Assert.Equal(
            DateTimeKind.Utc,
            report.ResolvedOnUtc!.Value.Kind);

        // History retained in full: nothing is erased on dismissal.
        Assert.Equal("not spam after review", report.Reason);
        Assert.Equal(alice, report.ReporterUserId);
        Assert.Equal(UtcStamp, report.CreatedOnUtc);

        UserId otherAdmin = UserId.New();
        Result doubleDismiss = await handler.Handle(
            new DismissReportCommand(otherAdmin, report.Id), default);

        Assert.True(doubleDismiss.IsFailure);
        Assert.Equal(
            ReportErrors.InvalidState.Code,
            doubleDismiss.Error.Code);

        // The rejected attempt keeps the first outcome and its audit.
        Assert.Equal(ReportStatus.Dismissed, report.Status);
        Assert.Equal(admin, report.ResolvedBy);

        Result unknown = await handler.Handle(
            new DismissReportCommand(admin, ReportId.New()), default);

        Assert.True(unknown.IsFailure);
        Assert.Equal(ReportErrors.NotFound.Code, unknown.Error.Code);
    }

    // ------------------------------------------------------------------
    // Endpoint 15 — uphold and the mutual block.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Uphold_ClosesTheReportAppliesBothBlockDirectionsAndDeniesBothSenders()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);
        Report report = SeedReport(
            db, conversation, alice, "harassment", UtcStamp);
        await db.SaveChangesAsync();

        UserId admin = UserId.New();
        var handler = new UpholdReportCommandHandler(db);

        Assert.True((await handler.Handle(
            new UpholdReportCommand(admin, report.Id), default)).IsSuccess);

        Assert.Equal(ReportStatus.Upheld, report.Status);
        Assert.Equal(admin, report.ResolvedBy);
        Assert.NotNull(report.ResolvedOnUtc);

        // Mutual: one row per direction between the conversation's two
        // participants, and exactly two rows in total.
        List<Block> blocks = await db.Blocks.ToListAsync();
        Assert.Equal(2, blocks.Count);
        Assert.Contains(blocks, block =>
            block.BlockerUserId == alice && block.BlockedUserId == bob);
        Assert.Contains(blocks, block =>
            block.BlockerUserId == bob && block.BlockedUserId == alice);

        // Provable through the send path: either party sending to the other
        // gets the same generic failure a non-participant gets — never a
        // block reveal — so the uphold really is enforced.
        var send = new SendMessageCommandHandler(db);

        Result<SendMessageResponse> aliceToBob = await send.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "client-1", "Text", "hi", null),
            default);
        Result<SendMessageResponse> bobToAlice = await send.Handle(
            new SendMessageCommand(
                bob, conversation.Id, "client-2", "Text", "hi", null),
            default);

        Assert.All(
            new[] { aliceToBob, bobToAlice },
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(
                    ConversationErrors.NotFound.Code,
                    result.Error.Code);
            });
        Assert.Empty(db.Messages);
    }

    [Fact]
    public async Task Uphold_AfterADismissIs409AndCreatesNoBlocksAtAll()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);
        Report report = SeedReport(
            db, conversation, alice, "spam", UtcStamp);
        await db.SaveChangesAsync();

        UserId admin = UserId.New();
        var dismiss = new DismissReportCommandHandler(db);
        var uphold = new UpholdReportCommandHandler(db);

        Assert.True((await dismiss.Handle(
            new DismissReportCommand(admin, report.Id), default)).IsSuccess);

        Result lateUphold = await uphold.Handle(
            new UpholdReportCommand(UserId.New(), report.Id), default);

        Assert.True(lateUphold.IsFailure);
        Assert.Equal(
            ReportErrors.InvalidState.Code,
            lateUphold.Error.Code);
        Assert.Empty(db.Blocks);
        Assert.Equal(ReportStatus.Dismissed, report.Status);

        Result unknown = await uphold.Handle(
            new UpholdReportCommand(admin, ReportId.New()), default);

        Assert.True(unknown.IsFailure);
        Assert.Equal(ReportErrors.NotFound.Code, unknown.Error.Code);
        Assert.Empty(db.Blocks);
    }

    [Fact]
    public async Task Uphold_ReUpHoldIs409AndNeverDuplicatesAnExistingBlockRow()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);
        Report report = SeedReport(
            db, conversation, alice, "harassment", UtcStamp);

        // Alice had already blocked Bob before the uphold: the idempotent
        // pre-check must keep that single row rather than add a second one.
        db.Blocks.Add(Block.Create(alice, bob, UtcStamp));
        await db.SaveChangesAsync();

        UserId admin = UserId.New();
        var handler = new UpholdReportCommandHandler(db);

        Assert.True((await handler.Handle(
            new UpholdReportCommand(admin, report.Id), default)).IsSuccess);

        Assert.Equal(2, await db.Blocks.CountAsync());

        Result reUphold = await handler.Handle(
            new UpholdReportCommand(admin, report.Id), default);

        Assert.True(reUphold.IsFailure);
        Assert.Equal(
            ReportErrors.InvalidState.Code,
            reUphold.Error.Code);

        // The rejected re-uphold touched nothing: still two rows, one per
        // direction, and the first resolution's audit.
        Assert.Equal(2, await db.Blocks.CountAsync());
        Assert.Equal(1, await db.Blocks.CountAsync(block =>
            block.BlockerUserId == alice && block.BlockedUserId == bob));
        Assert.Equal(ReportStatus.Upheld, report.Status);
        Assert.Equal(admin, report.ResolvedBy);
    }

    // ------------------------------------------------------------------
    // Controller — endpoints 11 and 12–15 wiring.
    // ------------------------------------------------------------------

    [Fact]
    public void ChatController_ExposesEndpoint11UnderAuthorizeOnTheChatRoute()
    {
        Assert.Equal(
            "api/v1/chat",
            Assert.Single(typeof(ChatController).GetCustomAttributes<RouteAttribute>()).Template);

        var authorize = Assert.Single(typeof(ChatController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Null(authorize.Policy);

        System.Reflection.MethodInfo submit =
            typeof(ChatController).GetMethod(nameof(ChatController.SubmitReport))!;

        Assert.Equal("reports", submit.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Contains(submit.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status201Created);
        Assert.Contains(submit.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Contains(submit.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound);
    }

    [Fact]
    public void AdminChatReportsController_ExposesEndpoints1215UnderTheChatSupportAdminPolicy()
    {
        // The wire name is pinned: the attribute, the constant, and the DI
        // registration all key on this exact string.
        Assert.Equal(
            "ChatSupportAdmin",
            AuthorizationPolicies.ChatSupportAdmin);

        var authorize = Assert.Single(typeof(AdminChatReportsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.ChatSupportAdmin, authorize.Policy);

        Assert.Equal(
            "api/v1/admin/chat/reports",
            Assert.Single(typeof(AdminChatReportsController)
                .GetCustomAttributes<RouteAttribute>()).Template);

        System.Reflection.MethodInfo list =
            typeof(AdminChatReportsController)
                .GetMethod(nameof(AdminChatReportsController.List))!;
        System.Reflection.MethodInfo detail =
            typeof(AdminChatReportsController)
                .GetMethod(nameof(AdminChatReportsController.Detail))!;
        System.Reflection.MethodInfo messages =
            typeof(AdminChatReportsController)
                .GetMethod(nameof(AdminChatReportsController.Messages))!;
        System.Reflection.MethodInfo dismiss =
            typeof(AdminChatReportsController)
                .GetMethod(nameof(AdminChatReportsController.Dismiss))!;
        System.Reflection.MethodInfo uphold =
            typeof(AdminChatReportsController)
                .GetMethod(nameof(AdminChatReportsController.Uphold))!;

        Assert.True(string.IsNullOrEmpty(
            list.GetCustomAttribute<HttpGetAttribute>()!.Template));
        Assert.Equal(
            "{reportId:guid}",
            detail.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(
            "{reportId:guid}/messages",
            messages.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(
            "{reportId:guid}/dismiss",
            dismiss.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(
            "{reportId:guid}/uphold",
            uphold.GetCustomAttribute<HttpPostAttribute>()!.Template);

        Assert.Contains(list.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK);
        Assert.Contains(detail.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound);
        Assert.Contains(messages.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK);
        Assert.Contains(dismiss.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status204NoContent);
        Assert.Contains(dismiss.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status409Conflict);
        Assert.Contains(uphold.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status204NoContent);
        Assert.Contains(uphold.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task AdminChatReportsController_DispatchesTheActorAndTheFrozenStatuses()
    {
        UserId admin = UserId.New();
        Guid reportId = Guid.NewGuid();

        // 204 on a successful resolve, with the JWT actor stamped in.
        var resolving = new CapturingSender(Result.Success());
        AdminChatReportsController resolveController =
            CreateAdminController(resolving, admin);

        Assert.IsType<NoContentResult>(
            await resolveController.Dismiss(reportId, default));
        DismissReportCommand dismiss =
            Assert.IsType<DismissReportCommand>(resolving.LastRequest);
        Assert.Equal(admin, dismiss.Admin);
        Assert.Equal(new ReportId(reportId), dismiss.ReportId);

        Assert.IsType<NoContentResult>(
            await resolveController.Uphold(reportId, default));
        UpholdReportCommand uphold =
            Assert.IsType<UpholdReportCommand>(resolving.LastRequest);
        Assert.Equal(admin, uphold.Admin);

        // Unknown report → plain 404 (Support paths need no privacy
        // forcing), with the code intact for clients to key on.
        var unknown = CreateAdminController(
            new CapturingSender(
                Result<ReportResponse>.Failure(ReportErrors.NotFound)),
            admin);

        IActionResult notFound = await unknown.Detail(reportId, default);

        ObjectResult notFoundObject = Assert.IsType<ObjectResult>(notFound);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundObject.StatusCode);
        var notFoundDetails = Assert.IsType<ProblemDetails>(notFoundObject.Value);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundDetails.Status);
        Assert.Equal(
            ReportErrors.NotFound.Code,
            notFoundDetails.Extensions["code"]);
        Assert.Equal(
            "The requested resource was not found.",
            notFoundDetails.Detail);

        // Double resolve → 409 with Chat.Report.InvalidState, which the
        // shared mapper would otherwise answer as a 400.
        var conflicted = CreateAdminController(
            new CapturingSender(Result.Failure(ReportErrors.InvalidState)),
            admin);

        IActionResult conflict = await conflicted.Dismiss(reportId, default);

        ObjectResult conflictObject = Assert.IsType<ObjectResult>(conflict);
        Assert.Equal(StatusCodes.Status409Conflict, conflictObject.StatusCode);
        var conflictDetails = Assert.IsType<ProblemDetails>(conflictObject.Value);
        Assert.Equal(StatusCodes.Status409Conflict, conflictDetails.Status);
        Assert.Equal(
            ReportErrors.InvalidState.Code,
            conflictDetails.Extensions["code"]);
        Assert.Equal(
            "The request conflicts with the current state.",
            conflictDetails.Detail);

        // The queue carries the frozen query and answers 200 with the
        // envelope; an unrecognised status filter is a 400.
        var listing = new CapturingSender(
            Result<PagedResult<ReportResponse>>.Success(
                new PagedResult<ReportResponse>([], 2, 10, 0, 0)));
        AdminChatReportsController listController =
            CreateAdminController(listing, admin);

        IActionResult ok = await listController.List("open", 2, 10, default);

        Assert.IsType<OkObjectResult>(ok);
        GetReportsQuery listQuery =
            Assert.IsType<GetReportsQuery>(listing.LastRequest);
        Assert.Equal("open", listQuery.Status);
        Assert.Equal(2, listQuery.Page);
        Assert.Equal(10, listQuery.PageSize);

        var invalid = CreateAdminController(
            new CapturingSender(
                Result<PagedResult<ReportResponse>>.Failure(
                    ReportErrors.Invalid)),
            admin);

        IActionResult badRequest = await invalid.List("bogus", 1, 20, default);

        ObjectResult badRequestObject = Assert.IsType<ObjectResult>(badRequest);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestObject.StatusCode);
        var invalidDetails = Assert.IsType<ProblemDetails>(badRequestObject.Value);
        Assert.Equal(ReportErrors.Invalid.Code, invalidDetails.Extensions["code"]);

        // The messages view dispatches by report id.
        var messaging = new CapturingSender(
            Result<IReadOnlyList<MessageItemResponse>>.Success([]));
        AdminChatReportsController messagesController =
            CreateAdminController(messaging, admin);

        Assert.IsType<OkObjectResult>(
            await messagesController.Messages(reportId, default));
        GetReportMessagesQuery messagesQuery =
            Assert.IsType<GetReportMessagesQuery>(messaging.LastRequest);
        Assert.Equal(new ReportId(reportId), messagesQuery.ReportId);
    }

    // ------------------------------------------------------------------
    // Harness.
    // ------------------------------------------------------------------

    private static Report CreateReport(
        string? reason,
        MessageId? messageId = null) =>
        Report.Create(
            ConversationId.New(),
            messageId,
            UserId.New(),
            reason!,
            UtcStamp);

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<
        (UserId Alice, UserId Bob, Conversation Conversation)>
        SeedConversationAsync(ChatDbContext db)
    {
        UserId alice = UserId.New();
        UserId bob = UserId.New();

        Conversation conversation = CreateDirect(alice, bob, UtcStamp);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        return (alice, bob, conversation);
    }

    private static Conversation CreateDirect(
        UserId participantA,
        UserId participantB,
        DateTime createdOnUtc) =>
        Conversation.Create(
            participantA,
            participantB,
            ConversationScope.Direct,
            null,
            createdOnUtc);

    private static Message SeedMessage(
        ChatDbContext db,
        Conversation conversation,
        UserId sender,
        DateTime sentOn,
        string text = "hi")
    {
        Message message = Message.Create(
            conversation.Id,
            sender,
            MessageType.Text,
            text,
            null,
            $"client-{Guid.NewGuid():N}",
            sentOn);

        db.Messages.Add(message);
        return message;
    }

    /// <summary>Seeds an Open report with an explicit creation stamp so the
    /// queue's newest-first order is deterministic.</summary>
    private static Report SeedReport(
        ChatDbContext db,
        Conversation conversation,
        UserId reporter,
        string reason,
        DateTime createdOnUtc,
        MessageId? messageId = null)
    {
        Report report = Report.Create(
            conversation.Id,
            messageId,
            reporter,
            reason,
            createdOnUtc);

        db.Reports.Add(report);
        return report;
    }

    private static ChatController CreateController(ISender sender, UserId? actor = null)
    {
        var controller = new ChatController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        if (actor.HasValue)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(JwtRegisteredClaimNames.Sub, actor.Value.Value.ToString())],
                    "test"));
        }

        return controller;
    }

    private static AdminChatReportsController CreateAdminController(
        ISender sender,
        UserId actor)
    {
        var controller = new AdminChatReportsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, actor.Value.ToString())],
                "test"));

        return controller;
    }

    private sealed class CapturingSender(object response) : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
