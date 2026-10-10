using System.Reflection;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Chat Message domain rules: text length boundary, file/text payload
/// separation, client idempotency key shape, UTC send stamp, and the
/// set-once delivered/read stamps. Messages are immutable once sent — the
/// only public mutators are the two stamp methods, which this fixture
/// also pins via reflection.
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
}
