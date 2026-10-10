using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.UnitTests.Chat;

public sealed class ConversationTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 0, 0), DateTimeKind.Utc);

    [Fact]
    public void Create_NormalizesParticipantPairSoEitherDirectionMatchesKey()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();

        Conversation conversation = Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            null,
            UtcStamp);

        Assert.True(conversation.MatchesKey(userA, userB, null));
        Assert.True(conversation.MatchesKey(userB, userA, null));
        Assert.Equal(userA.Value.CompareTo(userB.Value) <= 0
            ? userA
            : userB, conversation.ParticipantOne);
        Assert.Equal(userA.Value.CompareTo(userB.Value) <= 0
            ? userB
            : userA, conversation.ParticipantTwo);
    }

    [Fact]
    public void Create_KeysOpenOrGetOnPairPlusBookingReference()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();
        BookingId bookingOne = BookingId.New();
        BookingId bookingTwo = BookingId.New();

        Conversation direct = Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            null,
            UtcStamp);
        Conversation bookingScoped = Conversation.Create(
            userA,
            userB,
            ConversationScope.Booking,
            bookingOne,
            UtcStamp);

        Assert.True(direct.MatchesKey(userA, userB, null));
        Assert.False(direct.MatchesKey(userA, userB, bookingOne));

        Assert.True(bookingScoped.MatchesKey(userB, userA, bookingOne));
        Assert.False(bookingScoped.MatchesKey(userA, userB, null));
        Assert.False(bookingScoped.MatchesKey(userA, userB, bookingTwo));

        Assert.NotEqual(direct.Id, bookingScoped.Id);
        Assert.Equal(ConversationScope.Direct, direct.Scope);
        Assert.Equal(ConversationScope.Booking, bookingScoped.Scope);
        Assert.Equal(bookingOne, bookingScoped.BookingId);
    }

    [Fact]
    public void Create_SetsLastActivityFromCreationAndKeepsUtc()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);

        Assert.Equal(UtcStamp, conversation.CreatedOnUtc);
        Assert.Equal(UtcStamp, conversation.LastActivityOnUtc);
        Assert.Equal(DateTimeKind.Utc, conversation.LastActivityOnUtc.Kind);
    }

    [Fact]
    public void Create_RejectsSelfConversation()
    {
        UserId user = UserId.New();

        Assert.Throws<DomainException>(() => Conversation.Create(
            user,
            user,
            ConversationScope.Direct,
            null,
            UtcStamp));
    }

    [Fact]
    public void Create_RejectsMissingParticipant()
    {
        Assert.Throws<DomainException>(() => Conversation.Create(
            UserId.Empty,
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp));
    }

    [Fact]
    public void Create_RejectsInconsistentScopeAndBookingReference()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();
        BookingId bookingId = BookingId.New();

        Assert.Throws<DomainException>(() => Conversation.Create(
            userA,
            userB,
            ConversationScope.Booking,
            null,
            UtcStamp));

        Assert.Throws<DomainException>(() => Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            bookingId,
            UtcStamp));
    }

    [Fact]
    public void Create_RejectsNonUtcTimestamp()
    {
        DateTime localStamp = DateTime.SpecifyKind(
            new DateTime(2026, 10, 1, 12, 0, 0),
            DateTimeKind.Local);

        Assert.Throws<DomainException>(() => Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            localStamp));
    }

    [Fact]
    public void UpdateLastActivity_RejectsNonUtcTimestamp()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);

        Assert.Throws<DomainException>(() => conversation.UpdateLastActivity(
            new DateTime(2026, 10, 1, 13, 0, 0, DateTimeKind.Unspecified)));
        Assert.Equal(UtcStamp, conversation.LastActivityOnUtc);
    }

    [Fact]
    public void Hide_HidesForDeleterOnlyAndErasesNothing()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();
        Conversation conversation = Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            null,
            UtcStamp);

        conversation.Hide(userA);

        Assert.True(conversation.IsHiddenFor(userA));
        Assert.False(conversation.IsHiddenFor(userB));
        Assert.True(conversation.IsParticipant(userA));
        Assert.True(conversation.IsParticipant(userB));
        Assert.Equal(UtcStamp, conversation.CreatedOnUtc);
        Assert.Equal(UtcStamp, conversation.LastActivityOnUtc);
    }

    [Fact]
    public void Hide_ByNonParticipantThrows()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);
        UserId outsider = UserId.New();

        Assert.Throws<DomainException>(() => conversation.Hide(outsider));
        Assert.Throws<DomainException>(() => conversation.IsHiddenFor(outsider));
    }

    [Fact]
    public void Attachment_CreateNormalizesKeyAndRejectsInvalidInput()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);
        UserId uploader = UserId.New();

        Attachment attachment = Attachment.Create(
            conversation.Id,
            uploader,
            "  private/chat/file.webp  ",
            "  IMAGE/WEBP  ",
            2048,
            UtcStamp);

        Assert.Equal(conversation.Id, attachment.ConversationId);
        Assert.Equal(uploader, attachment.UploadedBy);
        Assert.Equal("private/chat/file.webp", attachment.StorageKey);
        Assert.Equal("image/webp", attachment.ContentType);
        Assert.Equal(2048, attachment.SizeBytes);
        Assert.False(attachment.IsBound);

        Assert.Throws<DomainException>(() => Attachment.Create(
            conversation.Id,
            uploader,
            " ",
            "image/webp",
            2048,
            UtcStamp));

        Assert.Throws<DomainException>(() => Attachment.Create(
            conversation.Id,
            UserId.Empty,
            "private/chat/file.webp",
            "image/webp",
            2048,
            UtcStamp));

        Assert.Throws<DomainException>(() => Attachment.Create(
            conversation.Id,
            uploader,
            "private/chat/file.webp",
            "image/webp",
            0,
            UtcStamp));
    }

    [Fact]
    public void Attachment_CreateRejectsNonUtcTimestamp()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);

        Assert.Throws<DomainException>(() => Attachment.Create(
            conversation.Id,
            UserId.New(),
            "private/chat/file.jpg",
            "image/jpeg",
            100,
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void Attachment_BindIsSingleUse()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);
        Attachment attachment = Attachment.Create(
            conversation.Id,
            UserId.New(),
            "private/chat/file.png",
            "image/png",
            512,
            UtcStamp);

        attachment.Bind();
        Assert.True(attachment.IsBound);

        Assert.Throws<DomainException>(() => attachment.Bind());
        Assert.True(attachment.IsBound);
    }

    [Fact]
    public void Block_CreateKeepsOrderedPairAndRejectsSelfBlock()
    {
        UserId blocker = UserId.New();
        UserId blocked = UserId.New();

        Block block = Block.Create(blocker, blocked, UtcStamp);

        Assert.Equal(blocker, block.BlockerUserId);
        Assert.Equal(blocked, block.BlockedUserId);
        Assert.Equal(UtcStamp, block.CreatedOnUtc);

        Assert.Throws<DomainException>(() => Block.Create(
            blocker,
            blocker,
            UtcStamp));
    }

    [Fact]
    public void Block_CreateRejectsNonUtcTimestamp()
    {
        Assert.Throws<DomainException>(() => Block.Create(
            UserId.New(),
            UserId.New(),
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Unspecified)));
    }
}
