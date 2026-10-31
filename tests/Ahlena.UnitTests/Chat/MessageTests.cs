using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Controllers;
using Ahlena.API.Controllers.Requests;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Attachments;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Chat.Application.Messages;
using Ahlena.Modules.Chat.Domain.Attachments;
using Ahlena.Modules.Chat.Domain.Blocks;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Chat;

/// <summary>
/// Chat Message rules. The first half pins the domain aggregate (text length
/// boundary, file/text payload separation, client idempotency key shape, UTC
/// send stamp, set-once delivered/read stamps, immutability). The second half
/// drives the Lane B handlers and controller actions of frozen endpoints 3,
/// 4, and 8: send/idempotency, the cursor page, delivery stamping, and the
/// unread totals.
/// </summary>
public sealed class MessageTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 8, 9, 0, 0), DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Create — text body boundaries.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_AcceptsTextOfExactlyTwoThousandCharacters()
    {
        string body = new('a', Message.MaximumTextLength);

        Message message = CreateText(body);

        Assert.Equal(Message.MaximumTextLength, message.Text!.Length);
        Assert.Equal(MessageType.Text, message.Type);
    }

    [Fact]
    public void Create_RejectsTextOverTwoThousandCharacters()
    {
        string body = new('a', Message.MaximumTextLength + 1);

        Assert.Throws<DomainException>(() => CreateText(body));
    }

    [Fact]
    public void Create_TrimsTheTextBody()
    {
        Message message = CreateText("  hello there  ");

        Assert.Equal("hello there", message.Text);
    }

    // ------------------------------------------------------------------
    // Create — payload separation by type.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_FileMessageCarriesNoTextAndAcceptsAnAttachment()
    {
        Message message = CreateFile(AttachmentId.New());

        Assert.Equal(MessageType.File, message.Type);
        Assert.Null(message.Text);
        Assert.NotNull(message.AttachmentId);
    }

    [Fact]
    public void Create_FileMessageWorksWithoutAnAttachment()
    {
        Message message = CreateFile(null);

        Assert.Equal(MessageType.File, message.Type);
        Assert.Null(message.Text);
        Assert.Null(message.AttachmentId);
    }

    [Fact]
    public void Create_TextMessageWithoutBodyIsRejected()
    {
        Assert.Throws<DomainException>(() => CreateText(null));
        Assert.Throws<DomainException>(() => CreateText(string.Empty));
        Assert.Throws<DomainException>(() => CreateText("   "));
    }

    // ------------------------------------------------------------------
    // Create — Location payload (Opus delta).
    // ------------------------------------------------------------------

    [Fact]
    public void Create_AcceptsAValidLocationPayloadAndTrimsTheLabel()
    {
        Message message = CreateLocation(30.0444, 31.2357, "  Cairo Tower  ");

        Assert.Equal(MessageType.Location, message.Type);
        Assert.Equal(30.0444, message.Latitude);
        Assert.Equal(31.2357, message.Longitude);
        Assert.Equal("Cairo Tower", message.LocationLabel);
        Assert.Null(message.Text);
        Assert.Null(message.AttachmentId);
    }

    [Fact]
    public void Create_AcceptsLocationWithoutALabelAndDropsABlankOne()
    {
        Assert.Null(CreateLocation(0, 0, null).LocationLabel);
        Assert.Null(CreateLocation(0, 0, "   ").LocationLabel);
    }

    [Theory]
    [InlineData(-91.0, 0.0)]
    [InlineData(91.0, 0.0)]
    [InlineData(0.0, -181.0)]
    [InlineData(0.0, 181.0)]
    public void Create_RejectsOutOfRangeCoordinates(
        double latitude,
        double longitude)
    {
        Assert.Throws<DomainException>(() =>
            CreateLocation(latitude, longitude, null));
    }

    [Fact]
    public void Create_RejectsLocationWithoutCoordinatesMixedPayloadsAndLongLabels()
    {
        ConversationId conversation = ConversationId.New();
        UserId sender = UserId.New();

        // Missing one or both coordinates.
        Assert.Throws<DomainException>(() => Message.Create(
            conversation, sender, MessageType.Location,
            null, null, "client-key-1", UtcStamp));
        Assert.Throws<DomainException>(() => Message.Create(
            conversation, sender, MessageType.Location,
            null, null, "client-key-1", UtcStamp, latitude: 30.0));

        // Location must not carry a body or an attachment.
        Assert.Throws<DomainException>(() => Message.Create(
            conversation, sender, MessageType.Location,
            "body", null, "client-key-1", UtcStamp,
            latitude: 30.0, longitude: 31.0));
        Assert.Throws<DomainException>(() => Message.Create(
            conversation, sender, MessageType.Location,
            null, AttachmentId.New(), "client-key-1", UtcStamp,
            latitude: 30.0, longitude: 31.0));

        // Other types must not carry a location payload.
        Assert.Throws<DomainException>(() => Message.Create(
            conversation, sender, MessageType.Text,
            "hi", null, "client-key-1", UtcStamp,
            latitude: 30.0, longitude: 31.0));

        // Oversized label.
        Assert.Throws<DomainException>(() => CreateLocation(
            30.0,
            31.0,
            new('l', Message.MaximumLocationLabelLength + 1)));
    }

    // ------------------------------------------------------------------
    // Create — client idempotency key shape.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_RejectsEmptyClientMessageId()
    {
        Assert.Throws<DomainException>(() => CreateText("hi", clientMessageId: string.Empty));
        Assert.Throws<DomainException>(() => CreateText("hi", clientMessageId: "   "));
    }

    [Fact]
    public void Create_RejectsOversizedClientMessageIdAndTrims()
    {
        string oversized = new('k', Message.MaximumClientMessageIdLength + 1);

        Assert.Throws<DomainException>(() => CreateText("hi", clientMessageId: oversized));

        Message message = CreateText("hi", clientMessageId: "  client-key-1  ");
        Assert.Equal("client-key-1", message.ClientMessageId);
    }

    // ------------------------------------------------------------------
    // Create — server-set UTC send stamp.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_RejectsNonUtcSendStamp()
    {
        DateTime local = DateTime.Now;
        DateTime unspecified = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Unspecified);

        Assert.Throws<DomainException>(() => CreateText("hi", sentOn: local));
        Assert.Throws<DomainException>(() => CreateText("hi", sentOn: unspecified));
    }

    [Fact]
    public void Create_SetsServerStampAndLeavesDeliveryReadNull()
    {
        Message message = CreateText("hi");

        Assert.Equal(UtcStamp, message.SentOnUtc);
        Assert.Equal(DateTimeKind.Utc, message.SentOnUtc.Kind);
        Assert.Null(message.DeliveredOnUtc);
        Assert.Null(message.ReadOnUtc);
    }

    // ------------------------------------------------------------------
    // MarkDelivered / MarkRead — set-once, UTC-validated.
    // ------------------------------------------------------------------

    [Fact]
    public void MarkDelivered_StampsOnceAndLaterFetchesNoOp()
    {
        Message message = CreateText("hi");
        DateTime firstFetch = UtcStamp.AddMinutes(5);

        message.MarkDelivered(firstFetch);
        message.MarkDelivered(firstFetch.AddMinutes(10));

        Assert.Equal(firstFetch, message.DeliveredOnUtc);
    }

    [Fact]
    public void MarkRead_StampsOnceAndLaterMarksNoOp()
    {
        Message message = CreateText("hi");
        DateTime firstMark = UtcStamp.AddMinutes(30);

        message.MarkRead(firstMark);
        message.MarkRead(firstMark.AddMinutes(10));

        Assert.Equal(firstMark, message.ReadOnUtc);
    }

    [Fact]
    public void MarkDeliveredAndMarkRead_RejectNonUtcEvenOnTheNoOpPath()
    {
        Message message = CreateText("hi");
        DateTime local = DateTime.Now;

        Assert.Throws<DomainException>(() => message.MarkDelivered(local));
        Assert.Null(message.DeliveredOnUtc);

        message.MarkDelivered(UtcStamp.AddMinutes(5));
        Assert.Throws<DomainException>(() => message.MarkDelivered(local));
        Assert.Equal(UtcStamp.AddMinutes(5), message.DeliveredOnUtc);

        Assert.Throws<DomainException>(() => message.MarkRead(local));
        Assert.Null(message.ReadOnUtc);

        message.MarkRead(UtcStamp.AddMinutes(10));
        Assert.Throws<DomainException>(() => message.MarkRead(local));
        Assert.Equal(UtcStamp.AddMinutes(10), message.ReadOnUtc);
    }

    [Fact]
    public void MarkRead_DoesNotTouchTheDeliveryStampAndViceVersa()
    {
        Message message = CreateText("hi");

        message.MarkDelivered(UtcStamp.AddMinutes(5));
        Assert.Null(message.ReadOnUtc);

        message.MarkRead(UtcStamp.AddMinutes(10));
        Assert.Equal(UtcStamp.AddMinutes(5), message.DeliveredOnUtc);
        Assert.Equal(UtcStamp.AddMinutes(10), message.ReadOnUtc);
    }

    // ------------------------------------------------------------------
    // Immutability — no mutators beyond the two stamps.
    // ------------------------------------------------------------------

    [Fact]
    public void Message_ExposesNoMutatorsBeyondTheTwoStampMethods()
    {
        // Compile-level intent made executable: the aggregate declares no
        // public instance method other than the set-once stamps (property
        // accessors excluded), so no edit/delete path can exist.
        string[] mutators = typeof(Message)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName) // drops get_/set_ accessors
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "MarkDelivered", "MarkRead" }, mutators);
    }

    // ------------------------------------------------------------------
    // Endpoint 4 — send.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Send_CreatesTheMessageMovesActivityAndReturnsTheFrozenItem()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        var handler = new SendMessageCommandHandler(db);

        Result<SendMessageResponse> result = await handler.Handle(
            new SendMessageCommand(
                alice,
                conversation.Id,
                "client-1",
                "Text",
                "  hello there  ",
                null),
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);

        MessageItemResponse item = result.Value.Message;
        Assert.Equal("client-1", item.ClientMessageId);
        Assert.Equal(alice.Value, item.SenderUserId);
        Assert.Equal("Text", item.Type);
        Assert.Equal("hello there", item.Text);
        Assert.Null(item.Attachment);
        Assert.Null(item.DeliveredOnUtc);
        Assert.Null(item.ReadOnUtc);

        Message stored = Assert.Single(db.Messages);
        Assert.Equal(stored.Id.Value, item.Id);
        Assert.Equal(stored.SentOnUtc, item.CreatedOnUtc);

        // Sending is the only thing that moves conversation activity, and it
        // carries the very stamp the message was created with.
        Assert.Equal(item.CreatedOnUtc, conversation.LastActivityOnUtc);
    }

    [Fact]
    public async Task Send_ExactReplayReturnsTheOriginalAndADifferentBodyConflicts()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);

        var handler = new SendMessageCommandHandler(db);

        Result<SendMessageResponse> created = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "client-1", "Text", "hi", null),
            default);

        Assert.True(created.Value.Created);

        Result<SendMessageResponse> replay = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "client-1", "Text", "hi", null),
            default);

        // Same key, same content: the original message, 200-shaped.
        Assert.True(replay.IsSuccess);
        Assert.False(replay.Value.Created);
        Assert.Equal(created.Value.Message.Id, replay.Value.Message.Id);

        Result<SendMessageResponse> conflict = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "client-1", "Text", "something else", null),
            default);

        Assert.True(conflict.IsFailure);
        Assert.Equal(MessageErrors.IdempotencyConflict.Code, conflict.Error.Code);

        // The conflict wrote nothing.
        Assert.Single(db.Messages);
    }

    [Fact]
    public async Task Send_LocationCarriesCoordinatesReplaysExactlyAndConflictsOnDifferentPayload()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);

        var handler = new SendMessageCommandHandler(db);

        Result<SendMessageResponse> created = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "loc-1", "Location",
                null, null, 30.0444, 31.2357, "Cairo Tower"),
            default);

        Assert.True(created.IsSuccess);
        Assert.True(created.Value.Created);

        MessageItemResponse item = created.Value.Message;
        Assert.Equal("Location", item.Type);
        Assert.Equal(conversation.Id.Value, item.ConversationId);
        Assert.Null(item.Text);
        Assert.Null(item.Attachment);
        Assert.Equal(30.0444, item.Location!.Latitude);
        Assert.Equal(31.2357, item.Location.Longitude);
        Assert.Equal("Cairo Tower", item.Location.Label);

        Result<SendMessageResponse> replay = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "loc-1", "Location",
                null, null, 30.0444, 31.2357, "Cairo Tower"),
            default);

        Assert.True(replay.IsSuccess);
        Assert.False(replay.Value.Created);
        Assert.Equal(created.Value.Message.Id, replay.Value.Message.Id);

        Result<SendMessageResponse> conflict = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "loc-1", "Location",
                null, null, 30.5, 31.2357, "Cairo Tower"),
            default);

        Assert.True(conflict.IsFailure);
        Assert.Equal(MessageErrors.IdempotencyConflict.Code, conflict.Error.Code);
        Assert.Single(db.Messages);
    }

    [Fact]
    public async Task Send_LocationRejectsMissingCoordinatesRangesAndMixedPayloads()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);

        var handler = new SendMessageCommandHandler(db);

        var missing = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "loc-missing", "Location",
                null, null, null, null, null),
            default);
        var ranged = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "loc-range", "Location",
                null, null, 95.0, 31.0, null),
            default);
        var withText = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "loc-text", "Location",
                "body", null, 30.0, 31.0, null),
            default);
        var withAttachment = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "loc-file", "Location",
                null, new AttachmentId(Guid.NewGuid()), 30.0, 31.0, null),
            default);
        var textWithLocation = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "text-loc", "Text",
                "hi", null, 30.0, 31.0, null),
            default);
        var unknownType = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "loc-type", "Pin",
                null, null, 30.0, 31.0, null),
            default);

        Assert.All(
            new[] { missing, ranged, withText, withAttachment, textWithLocation, unknownType },
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(MessageErrors.Invalid.Code, result.Error.Code);
            });
        Assert.Empty(db.Messages);
    }

    [Fact]
    public async Task GetMessages_ItemsCarryConversationIdCreatedOnUtcAndLocation()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        Message text = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(1), "first");
        Message location = Message.Create(
            conversation.Id,
            bob,
            MessageType.Location,
            null,
            null,
            "client-loc-1",
            UtcStamp.AddMinutes(2),
            30.0,
            31.0,
            "Home");
        db.Messages.Add(location);
        await db.SaveChangesAsync();

        var handler = new GetMessagesQueryHandler(db);

        Result<MessageCursorPage> result = await handler.Handle(
            new GetMessagesQuery(alice, conversation.Id, null, 30),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);

        MessageItemResponse newest = result.Value.Items[0];
        Assert.Equal(location.Id.Value, newest.Id);
        Assert.Equal(conversation.Id.Value, newest.ConversationId);
        Assert.Equal(location.SentOnUtc, newest.CreatedOnUtc);
        Assert.Equal(30.0, newest.Location!.Latitude);
        Assert.Equal("Home", newest.Location.Label);

        MessageItemResponse oldest = result.Value.Items[1];
        Assert.Equal(text.Id.Value, oldest.Id);
        Assert.Equal(conversation.Id.Value, oldest.ConversationId);
        Assert.Equal(text.SentOnUtc, oldest.CreatedOnUtc);
        Assert.Null(oldest.Location);
    }

    [Fact]
    public async Task Send_UnknownForeignAndHiddenConversationsShareOnePrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        UserId carol = UserId.New();
        UserId dave = UserId.New();
        Conversation foreign = CreateDirect(carol, dave, UtcStamp);
        db.Conversations.Add(foreign);

        conversation.Hide(alice);
        await db.SaveChangesAsync();

        var handler = new SendMessageCommandHandler(db);

        Result<SendMessageResponse> unknown = await handler.Handle(
            new SendMessageCommand(
                alice, ConversationId.New(), "client-1", "Text", "hi", null),
            default);

        Result<SendMessageResponse> notAParticipant = await handler.Handle(
            new SendMessageCommand(
                alice, foreign.Id, "client-1", "Text", "hi", null),
            default);

        Result<SendMessageResponse> hidden = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "client-1", "Text", "hi", null),
            default);

        Assert.All(
            new[] { unknown, notAParticipant, hidden },
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
    public async Task Send_TheBlockedSenderIsDeniedWhileTheBlockerMayStillSend()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        // Bob blocked Alice: Alice's send must die on the same generic 404 a
        // non-participant gets, never a block code.
        db.Blocks.Add(Block.Create(bob, alice, UtcStamp));
        await db.SaveChangesAsync();

        var handler = new SendMessageCommandHandler(db);

        Result<SendMessageResponse> denied = await handler.Handle(
            new SendMessageCommand(
                alice, conversation.Id, "client-1", "Text", "hi", null),
            default);

        Assert.True(denied.IsFailure);
        Assert.Equal(ConversationErrors.NotFound.Code, denied.Error.Code);
        Assert.Empty(db.Messages);

        // The other direction is not a denial: the blocker still sends.
        Result<SendMessageResponse> allowed = await handler.Handle(
            new SendMessageCommand(
                bob, conversation.Id, "client-1", "Text", "hi", null),
            default);

        Assert.True(allowed.IsSuccess);
        Assert.Single(db.Messages);
    }

    [Fact]
    public async Task Send_MalformedBodiesAreTheCallerInvalid400()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, _, Conversation conversation) =
            await SeedConversationAsync(db);

        var handler = new SendMessageCommandHandler(db);

        Result<SendMessageResponse> unknownType = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: "Message", text: "hi");
        Result<SendMessageResponse> numericType = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: "1", text: "hi");
        Result<SendMessageResponse> missingType = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: null, text: "hi");
        Result<SendMessageResponse> textWithAttachment = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: "Text", text: "hi",
            attachmentId: AttachmentId.New());
        Result<SendMessageResponse> fileWithBody = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: "File", text: "hi",
            attachmentId: AttachmentId.New());
        Result<SendMessageResponse> fileWithoutAttachment = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: "File", text: null);
        Result<SendMessageResponse> blankKey = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "   ", type: "Text", text: "hi");
        Result<SendMessageResponse> emptyBody = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: "Text", text: null);
        Result<SendMessageResponse> oversizedBody = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: "Text",
            text: new string('a', Message.MaximumTextLength + 1));

        Result<SendMessageResponse>[] failures =
        [
            unknownType,
            numericType,
            missingType,
            textWithAttachment,
            fileWithBody,
            fileWithoutAttachment,
            blankKey,
            emptyBody,
            oversizedBody
        ];

        Assert.All(
            failures,
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(MessageErrors.Invalid.Code, result.Error.Code);
            });

        Assert.Empty(db.Messages);
    }

    [Fact]
    public async Task Send_FileMessagesBindTheirOwnAttachmentAndNothingElse()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        UserId carol = UserId.New();
        UserId dave = UserId.New();
        Conversation other = CreateDirect(carol, dave, UtcStamp);
        db.Conversations.Add(other);

        Attachment mine = SeedAttachment(db, conversation, alice);
        Attachment someoneElses = SeedAttachment(db, conversation, bob);
        Attachment elsewhere = SeedAttachment(db, other, alice);
        Attachment alreadyBound = SeedAttachment(db, conversation, alice);
        db.Messages.Add(Message.Create(
            conversation.Id,
            alice,
            MessageType.File,
            null,
            alreadyBound.Id,
            "client-seed",
            UtcStamp.AddMinutes(-5)));
        alreadyBound.Bind();
        await db.SaveChangesAsync();

        var handler = new SendMessageCommandHandler(db);

        Result<SendMessageResponse> unknown = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-1", type: "File", attachmentId: AttachmentId.New());

        Result<SendMessageResponse> otherConversation = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-2", type: "File", attachmentId: elsewhere.Id);

        Result<SendMessageResponse> otherUploader = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-3", type: "File", attachmentId: someoneElses.Id);

        Result<SendMessageResponse> bound = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-4", type: "File", attachmentId: alreadyBound.Id);

        Assert.All(
            new[] { unknown, otherConversation, otherUploader },
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(AttachmentErrors.NotFound.Code, result.Error.Code);
            });

        // An attachment the sender owns but that is already bound is the
        // caller's own mistake, not an existence probe: 400.
        Assert.True(bound.IsFailure);
        Assert.Equal(MessageErrors.Invalid.Code, bound.Error.Code);

        Result<SendMessageResponse> sent = await SendAsync(
            handler, alice, conversation.Id,
            clientMessageId: "client-5", type: "File", attachmentId: mine.Id);

        Assert.True(sent.IsSuccess);
        Assert.Equal(MessageType.File.ToString(), sent.Value.Message.Type);
        Assert.Null(sent.Value.Message.Text);
        Assert.Equal(mine.Id.Value, sent.Value.Message.Attachment!.AttachmentId);
        Assert.True(db.Attachments.Single(item => item.Id == mine.Id).IsBound);

        // Only the two accepted attempts exist: the seeded row plus this one.
        Assert.Equal(2, db.Messages.Count());
    }

    // ------------------------------------------------------------------
    // Endpoint 3 — cursor page.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetMessages_ReturnsNewestFirstAndPagesOnTheCursor()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        Message first = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(1));
        Message second = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(2));
        Message third = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(3));
        await db.SaveChangesAsync();

        var handler = new GetMessagesQueryHandler(db);

        Result<MessageCursorPage> pageOne = await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, null, 2),
            default);

        Assert.True(pageOne.IsSuccess);
        Assert.Equal(
            new[] { third.Id.Value, second.Id.Value },
            pageOne.Value.Items.Select(item => item.Id));
        Assert.True(pageOne.Value.HasMore);
        Assert.NotNull(pageOne.Value.NextCursor);

        Result<MessageCursorPage> pageTwo = await handler.Handle(
            new GetMessagesQuery(
                bob,
                conversation.Id,
                pageOne.Value.NextCursor,
                2),
            default);

        Assert.Equal(
            new[] { first.Id.Value },
            pageTwo.Value.Items.Select(item => item.Id));
        Assert.False(pageTwo.Value.HasMore);
        Assert.Null(pageTwo.Value.NextCursor);

        // A raw message id is the documented second form of the parameter.
        Result<MessageCursorPage> byRawId = await handler.Handle(
            new GetMessagesQuery(
                bob, conversation.Id, second.Id.Value.ToString(), 2),
            default);

        Assert.Equal(
            new[] { first.Id.Value },
            byRawId.Value.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetMessages_ClampsTheLimitToTheFrozenBounds()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(1));
        SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(2));
        SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(3));
        await db.SaveChangesAsync();

        var handler = new GetMessagesQueryHandler(db);

        Result<MessageCursorPage> tooSmall = await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, null, 0),
            default);
        Result<MessageCursorPage> tooLarge = await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, null, 1000),
            default);

        // 0 clamps to 1, so there is more to page.
        Assert.Single(tooSmall.Value.Items);
        Assert.True(tooSmall.Value.HasMore);

        Assert.Equal(3, tooLarge.Value.Items.Count);
        Assert.False(tooLarge.Value.HasMore);
        Assert.Null(tooLarge.Value.NextCursor);
    }

    [Fact]
    public async Task GetMessages_StampsDeliveryForTheRecipientOnlyOnce()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        Message fromAlice = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(1));
        Message fromBob = SeedMessage(db, conversation, bob, UtcStamp.AddMinutes(2));
        await db.SaveChangesAsync();

        var handler = new GetMessagesQueryHandler(db);

        await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, null, 30), default);

        Assert.NotNull(db.Messages.Single(m => m.Id == fromAlice.Id).DeliveredOnUtc);
        // A sender never stamps their own message.
        Assert.Null(db.Messages.Single(m => m.Id == fromBob.Id).DeliveredOnUtc);

        DateTime firstFetch =
            db.Messages.Single(m => m.Id == fromAlice.Id).DeliveredOnUtc!.Value;

        await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, null, 30), default);

        // Set-once: the second fetch does not move the stamp.
        Assert.Equal(
            firstFetch,
            db.Messages.Single(m => m.Id == fromAlice.Id).DeliveredOnUtc);

        await handler.Handle(
            new GetMessagesQuery(alice, conversation.Id, null, 30), default);

        Assert.NotNull(db.Messages.Single(m => m.Id == fromBob.Id).DeliveredOnUtc);
        Assert.Equal(
            firstFetch,
            db.Messages.Single(m => m.Id == fromAlice.Id).DeliveredOnUtc);
    }

    [Fact]
    public async Task GetMessages_AnUnresolvableOrForeignCursorIsTheMessagePrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        UserId carol = UserId.New();
        UserId dave = UserId.New();
        Conversation other = CreateDirect(carol, dave, UtcStamp);
        db.Conversations.Add(other);
        Message elsewhere = Message.Create(
            other.Id,
            carol,
            MessageType.Text,
            "elsewhere",
            null,
            "client-elsewhere",
            UtcStamp);
        db.Messages.Add(elsewhere);
        await db.SaveChangesAsync();

        var handler = new GetMessagesQueryHandler(db);

        Result<MessageCursorPage> garbage = await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, "not-a-cursor", 30),
            default);

        Result<MessageCursorPage> unknownId = await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, Guid.NewGuid().ToString(), 30),
            default);

        Result<MessageCursorPage> otherConversation = await handler.Handle(
            new GetMessagesQuery(
                bob, conversation.Id, elsewhere.Id.Value.ToString(), 30),
            default);

        Assert.All(
            new[] { garbage, unknownId, otherConversation },
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(MessageErrors.NotFound.Code, result.Error.Code);
            });
    }

    [Fact]
    public async Task GetMessages_HiddenAndForeignConversationsAreTheConversationPrivacy404()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        UserId carol = UserId.New();
        UserId dave = UserId.New();
        Conversation foreign = CreateDirect(carol, dave, UtcStamp);
        db.Conversations.Add(foreign);

        conversation.Hide(bob);
        await db.SaveChangesAsync();

        var handler = new GetMessagesQueryHandler(db);

        Result<MessageCursorPage> hidden = await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, null, 30),
            default);

        Result<MessageCursorPage> notAParticipant = await handler.Handle(
            new GetMessagesQuery(bob, foreign.Id, null, 30),
            default);

        Result<MessageCursorPage> unknown = await handler.Handle(
            new GetMessagesQuery(bob, ConversationId.New(), null, 30),
            default);

        Assert.All(
            new[] { hidden, notAParticipant, unknown },
            result =>
            {
                Assert.True(result.IsFailure);
                Assert.Equal(
                    ConversationErrors.NotFound.Code,
                    result.Error.Code);
            });
    }

    [Fact]
    public async Task GetMessages_ShowsTheReadTickTheOtherParticipantTook()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        Message first = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(1));
        Message second = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(2));
        DateTime bobReadAt = UtcStamp.AddMinutes(10);
        db.ReadStates.Add(ConversationReadState.Create(
            conversation.Id, bob, second.Id.Value, bobReadAt));
        await db.SaveChangesAsync();

        var handler = new GetMessagesQueryHandler(db);

        Result<MessageCursorPage> asSender = await handler.Handle(
            new GetMessagesQuery(alice, conversation.Id, null, 30), default);
        Result<MessageCursorPage> asRecipient = await handler.Handle(
            new GetMessagesQuery(bob, conversation.Id, null, 30), default);

        // For the sender, the reader of their own message is the other
        // participant — so Bob's mark is what the tick reports.
        Assert.Equal(
            new DateTime?[] { bobReadAt, bobReadAt },
            asSender.Value.Items.Select(item => item.ReadOnUtc));

        // The recipient reads their own mark the same way.
        Assert.Equal(
            new DateTime?[] { bobReadAt, bobReadAt },
            asRecipient.Value.Items.Select(item => item.ReadOnUtc));

        Assert.Equal(first.Id.Value, asSender.Value.Items[^1].Id);
    }

    // ------------------------------------------------------------------
    // Endpoint 8 — unread totals.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetUnreadCount_CountsOnlyIncomingUnreadMessagesPerConversation()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(1));
        SeedMessage(db, conversation, bob, UtcStamp.AddMinutes(2));
        SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(3));
        await db.SaveChangesAsync();

        var handler = new GetUnreadCountQueryHandler(db);

        Result<UnreadCountResponse> forBob = await handler.Handle(
            new GetUnreadCountQuery(bob), default);
        Result<UnreadCountResponse> forAlice = await handler.Handle(
            new GetUnreadCountQuery(alice), default);

        // Bob never sent the two messages Alice did; Alice has only Bob's.
        Assert.Equal(2, forBob.Value.UnreadMessagesCount);
        Assert.Equal(1, forBob.Value.UnreadConversationsCount);

        Assert.Equal(1, forAlice.Value.UnreadMessagesCount);
        Assert.Equal(1, forAlice.Value.UnreadConversationsCount);
    }

    [Fact]
    public async Task GetUnreadCount_DropsToZeroOnceTheWatermarkOrStampCoversTheMessage()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        Message first = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(1));
        Message second = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(2));
        await db.SaveChangesAsync();

        var handler = new GetUnreadCountQueryHandler(db);

        // A watermark at the newest message covers everything up to it...
        db.ReadStates.Add(ConversationReadState.Create(
            conversation.Id, bob, second.Id.Value, UtcStamp.AddMinutes(5)));
        await db.SaveChangesAsync();

        Result<UnreadCountResponse> covered = await handler.Handle(
            new GetUnreadCountQuery(bob), default);

        Assert.Equal(0, covered.Value.UnreadMessagesCount);
        Assert.Equal(0, covered.Value.UnreadConversationsCount);

        // ...and a message's own set-once stamp counts as read outright.
        db.ReadStates.Remove(db.ReadStates.Single());
        first.MarkRead(UtcStamp.AddMinutes(6));
        second.MarkRead(UtcStamp.AddMinutes(7));
        await db.SaveChangesAsync();

        Result<UnreadCountResponse> stamped = await handler.Handle(
            new GetUnreadCountQuery(bob), default);

        Assert.Equal(0, stamped.Value.UnreadMessagesCount);
    }

    [Fact]
    public async Task GetUnreadCount_ExcludesHiddenAndForeignConversations()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        UserId carol = UserId.New();
        UserId dave = UserId.New();
        Conversation foreign = CreateDirect(carol, dave, UtcStamp);
        db.Conversations.Add(foreign);
        SeedMessage(db, foreign, carol, UtcStamp.AddMinutes(1));

        SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(2));

        // Hiding removes the conversation from every surface, badge included.
        conversation.Hide(bob);
        await db.SaveChangesAsync();

        var handler = new GetUnreadCountQueryHandler(db);

        Result<UnreadCountResponse> forBob = await handler.Handle(
            new GetUnreadCountQuery(bob), default);
        Result<UnreadCountResponse> forDave = await handler.Handle(
            new GetUnreadCountQuery(dave), default);
        Result<UnreadCountResponse> forStranger = await handler.Handle(
            new GetUnreadCountQuery(UserId.New()), default);

        Assert.Equal(0, forBob.Value.UnreadMessagesCount);
        Assert.Equal(0, forBob.Value.UnreadConversationsCount);

        Assert.Equal(1, forDave.Value.UnreadMessagesCount);
        Assert.Equal(1, forDave.Value.UnreadConversationsCount);

        // A requester with no visible conversations answers 0/0, not a 404.
        Assert.Equal(0, forStranger.Value.UnreadMessagesCount);
        Assert.Equal(0, forStranger.Value.UnreadConversationsCount);
    }

    [Fact]
    public async Task ConversationsList_LastMessageAndUnreadCountShareTheMessageRule()
    {
        await using ChatDbContext db = CreateDb();
        (UserId alice, UserId bob, Conversation conversation) =
            await SeedConversationAsync(db);

        Message older = SeedMessage(db, conversation, alice, UtcStamp.AddMinutes(1), "first");
        Message newer = SeedMessage(db, conversation, bob, UtcStamp.AddMinutes(2), "second");
        await db.SaveChangesAsync();

        var lookup = new FakeChatUserLookup();
        lookup.Add(alice, "Alice");
        var handler = new GetConversationsQueryHandler(db, lookup, new FakeChatFacilityLookup());

        Result<PagedResult<ConversationListItemResponse>> result =
            await handler.Handle(new GetConversationsQuery(bob, 1, 20), default);

        ConversationListItemResponse item = Assert.Single(result.Value.Items);

        Assert.Equal(newer.Id.Value, item.LastMessage!.Id);
        Assert.Equal("second", item.LastMessage.Text);
        Assert.Equal(MessageType.Text.ToString(), item.LastMessage.Type);
        Assert.Equal(newer.SentOnUtc, item.LastMessage.CreatedOnUtc);

        // Alice's message is still unread for Bob; his own is not.
        Assert.Equal(1, item.UnreadCount);
        Assert.NotEqual(older.Id.Value, item.LastMessage.Id);
    }

    // ------------------------------------------------------------------
    // Controller — endpoints 3, 4, and 8 status mapping.
    // ------------------------------------------------------------------

    [Fact]
    public async Task SendMessage_Answers201OnCreateAnd200OnExactReplay()
    {
        UserId actor = UserId.New();
        MessageItemResponse item = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "client-1",
            actor.Value,
            "Text",
            "hi",
            null,
            null,
            UtcStamp,
            null,
            null);

        var created = CreateController(
            new CapturingSender(
                Result<SendMessageResponse>.Success(
                    new SendMessageResponse(item, Created: true))),
            actor);

        IActionResult createdResult = await created.SendMessage(
            Guid.NewGuid(),
            new ChatSendMessageRequest("client-1", "Text", "hi", null),
            default);

        ObjectResult createdObject = Assert.IsType<ObjectResult>(createdResult);
        Assert.Equal(StatusCodes.Status201Created, createdObject.StatusCode);
        Assert.Same(item, createdObject.Value);

        var replayed = CreateController(
            new CapturingSender(
                Result<SendMessageResponse>.Success(
                    new SendMessageResponse(item, Created: false))),
            actor);

        IActionResult replayResult = await replayed.SendMessage(
            Guid.NewGuid(),
            new ChatSendMessageRequest("client-1", "Text", "hi", null),
            default);

        ObjectResult replayObject = Assert.IsType<OkObjectResult>(replayResult);
        Assert.Equal(StatusCodes.Status200OK, replayObject.StatusCode);
        Assert.Same(item, replayObject.Value);
    }

    [Fact]
    public async Task SendMessage_ForcesTheIdempotencyConflictTo409WithItsCode()
    {
        UserId actor = UserId.New();
        Guid conversationId = Guid.NewGuid();
        var sender = new CapturingSender(
            Result<SendMessageResponse>.Failure(
                MessageErrors.IdempotencyConflict));
        var controller = CreateController(sender, actor);

        IActionResult result = await controller.SendMessage(
            conversationId,
            new ChatSendMessageRequest("client-1", "Text", "hi", null),
            default);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problemDetails.Status);
        Assert.Equal(
            MessageErrors.IdempotencyConflict.Code,
            problemDetails.Extensions["code"]);

        // The command was built from the frozen wire shape.
        SendMessageCommand command = Assert.IsType<SendMessageCommand>(sender.LastRequest);

        Assert.Equal(actor, command.Requester);
        Assert.Equal(new ConversationId(conversationId), command.ConversationId);
        Assert.Equal("client-1", command.ClientMessageId);
        Assert.Equal("Text", command.Type);
        Assert.Equal("hi", command.Text);
        Assert.Null(command.AttachmentId);
    }

    [Fact]
    public async Task GetMessages_BuildsTheFrozenCommandAndMapsIts404ToTheGenericDetail()
    {
        UserId actor = UserId.New();
        Guid conversationId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();

        var sender = new CapturingSender(
            Result<MessageCursorPage>.Success(new MessageCursorPage([], null, false)));
        var controller = CreateController(sender, actor);

        IActionResult result = await controller.GetMessages(
            conversationId, messageId.ToString(), 17, default);

        Assert.IsType<OkObjectResult>(result);

        GetMessagesQuery query = Assert.IsType<GetMessagesQuery>(sender.LastRequest);

        Assert.Equal(actor, query.Requester);
        Assert.Equal(new ConversationId(conversationId), query.ConversationId);
        Assert.Equal(messageId.ToString(), query.BeforeId);
        Assert.Equal(17, query.PageSize);

        // An unresolvable cursor is the message privacy 404, forced to the
        // same generic body as every other Chat 404.
        var failing = CreateController(
            new CapturingSender(Result<MessageCursorPage>.Failure(
                MessageErrors.NotFound)), actor);

        IActionResult denied = await failing.GetMessages(
            conversationId, null, 30, default);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(denied);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(
            "The requested resource was not found.",
            problemDetails.Detail);
        Assert.Equal(
            MessageErrors.NotFound.Code,
            problemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task GetUnreadCount_SendsTheRequesterCommand()
    {
        UserId actor = UserId.New();

        var sender = new CapturingSender(
            Result<UnreadCountResponse>.Success(new UnreadCountResponse(0, 0)));
        var controller = CreateController(sender, actor);

        IActionResult result = await controller.GetUnreadCount(default);

        Assert.IsType<OkObjectResult>(result);

        GetUnreadCountQuery query =
            Assert.IsType<GetUnreadCountQuery>(sender.LastRequest);

        Assert.Equal(actor, query.Requester);
    }

    // ------------------------------------------------------------------
    // Harness.
    // ------------------------------------------------------------------

    private static Message CreateText(
        string? text,
        string clientMessageId = "client-key-1",
        DateTime? sentOn = null) =>
        Message.Create(
            ConversationId.New(),
            UserId.New(),
            MessageType.Text,
            text,
            null,
            clientMessageId,
            sentOn ?? UtcStamp);

    private static Message CreateFile(AttachmentId? attachmentId) =>
        Message.Create(
            ConversationId.New(),
            UserId.New(),
            MessageType.File,
            null,
            attachmentId,
            "client-key-1",
            UtcStamp);

    private static Message CreateLocation(
        double latitude,
        double longitude,
        string? label) =>
        Message.Create(
            ConversationId.New(),
            UserId.New(),
            MessageType.Location,
            null,
            null,
            "client-key-1",
            UtcStamp,
            latitude,
            longitude,
            label);

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

    private static Attachment SeedAttachment(
        ChatDbContext db,
        Conversation conversation,
        UserId uploader)
    {
        // Live upload instant: unbound uploads expire 24h after upload, so
        // fixtures pin the current time rather than the historical UtcStamp.
        Attachment attachment = Attachment.Create(
            conversation.Id,
            uploader,
            "storage/key.jpg",
            "image/jpeg",
            1024,
            DateTime.UtcNow);

        db.Attachments.Add(attachment);
        return attachment;
    }

    private static Task<Result<SendMessageResponse>> SendAsync(
        SendMessageCommandHandler handler,
        UserId requester,
        ConversationId conversationId,
        string? clientMessageId,
        string? type,
        string? text = null,
        AttachmentId? attachmentId = null) =>
        handler.Handle(
            new SendMessageCommand(
                requester,
                conversationId,
                clientMessageId,
                type,
                text,
                attachmentId),
            default);

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

    private sealed class FakeChatUserLookup : IChatUserLookup
    {
        private readonly Dictionary<UserId, UserProfile> _profiles = new();

        public void Add(
            UserId userId,
            string displayName,
            string? avatarUrl = null,
            int? accountType = null) =>
            _profiles[userId] = new UserProfile(
                userId, displayName, avatarUrl, accountType);

        public Task<UserProfile?> GetProfileAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _profiles.TryGetValue(userId, out UserProfile? profile)
                    ? profile
                    : null);
    }

    private sealed class FakeChatFacilityLookup : IChatFacilityLookup
    {
        private readonly Dictionary<UserId, FacilityPresentation> _facilities = new();

        public void Set(UserId ownerUserId, FacilityPresentation presentation) =>
            _facilities[ownerUserId] = presentation;

        public Task<FacilityPresentation?> GetForOwnerAsync(
            UserId ownerUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _facilities.TryGetValue(ownerUserId, out FacilityPresentation? presentation)
                    ? presentation
                    : null);
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
