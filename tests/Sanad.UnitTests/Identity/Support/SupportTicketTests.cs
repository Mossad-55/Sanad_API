using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Support;

public sealed class SupportTicketTests
{
    [Fact]
    public void Create_ValidTicket_ShouldTrimAndSetProperties()
    {
        UserId userId = UserId.New();

        var ticket = SupportTicket.Create(
            userId,
            "  Help needed  ",
            "  I need assistance with my account.  ",
            new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc));

        Assert.Equal(userId, ticket.UserId);
        Assert.Equal("Help needed", ticket.Subject);
        Assert.Equal("I need assistance with my account.", ticket.Message);
        Assert.Equal(SupportTicketStatus.New, ticket.Status);
        Assert.Equal(
            new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc),
            ticket.CreatedOnUtc);
        Assert.Null(ticket.NotifiedOnUtc);
        Assert.False(ticket.IsNotified);
    }

    [Fact]
    public void Create_InvalidTicket_ShouldThrowDomainException()
    {
        var utcNow = new DateTime(
            2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

        Assert.Throws<DomainException>(() =>
            SupportTicket.Create(
                UserId.Empty,
                "Subject",
                "Message",
                utcNow));

        Assert.Throws<DomainException>(() =>
            SupportTicket.Create(
                UserId.New(),
                "",
                "Message",
                utcNow));

        Assert.Throws<DomainException>(() =>
            SupportTicket.Create(
                UserId.New(),
                "Subject",
                "",
                utcNow));

        Assert.Throws<DomainException>(() =>
            SupportTicket.Create(
                UserId.New(),
                new string('S', 151),
                "Message",
                utcNow));

        Assert.Throws<DomainException>(() =>
            SupportTicket.Create(
                UserId.New(),
                "Subject",
                new string('M', 2001),
                utcNow));

        Assert.Throws<DomainException>(() =>
            SupportTicket.Create(
                UserId.New(),
                "Subject",
                "Message",
                new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Local)));
    }
}
