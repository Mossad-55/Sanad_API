using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Support;
using Ahlena.Modules.Identity.Domain.Support;
using Ahlena.UnitTests.Identity.Registration;

namespace Ahlena.UnitTests.Identity.Support;

/// <summary>
/// F5 own-ticket reads. A signed-in user sees only their own tickets, newest
/// first, with bounded paging; another user's ticket resolves as not found so
/// the surface never confirms or leaks someone else's ticket.
/// </summary>
public sealed class MySupportTicketTests
{
    private static readonly DateTime Base =
        new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static readonly UserId AdminId = UserId.New();

    [Fact]
    public async Task List_ReturnsOnlyTheCallersTickets_NewestFirst_WithTotalCount()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        UserId owner = UserId.New();
        UserId other = UserId.New();

        await SeedTicketAsync(dbContext, owner, "Oldest", Base.AddHours(-3));
        await SeedTicketAsync(dbContext, owner, "Newest", Base.AddHours(-1));
        await SeedTicketAsync(dbContext, owner, "Middle", Base.AddHours(-2));
        await SeedTicketAsync(dbContext, other, "Not mine", Base);

        GetMySupportTicketsQueryHandler handler = new(dbContext);

        Result<MySupportTicketsPage> result =
            await handler.Handle(
                new GetMySupportTicketsQuery(owner),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(25, result.Value.PageSize);
        Assert.Equal(
            new[] { "Newest", "Middle", "Oldest" },
            result.Value.Items.Select(item => item.Subject));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-5, 25)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(1000, 100)]
    public async Task List_ClampsTheRequestedPageSize(
        int requestedPageSize,
        int expectedPageSize)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        UserId owner = UserId.New();
        await SeedTicketAsync(dbContext, owner, "Only one", Base);

        GetMySupportTicketsQueryHandler handler = new(dbContext);

        Result<MySupportTicketsPage> result =
            await handler.Handle(
                new GetMySupportTicketsQuery(
                    owner,
                    PageSize: requestedPageSize),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPageSize, result.Value.PageSize);
    }

    [Fact]
    public async Task List_NormalisesAPageBelowOne()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        UserId owner = UserId.New();
        await SeedTicketAsync(dbContext, owner, "Only one", Base);

        GetMySupportTicketsQueryHandler handler = new(dbContext);

        Result<MySupportTicketsPage> result =
            await handler.Handle(
                new GetMySupportTicketsQuery(owner, Page: 0),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Page);
    }

    [Fact]
    public async Task List_ReturnsAnEmptyPage_ForAUserWithoutTickets()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        await SeedTicketAsync(
            dbContext,
            UserId.New(),
            "Someone else's",
            Base);

        GetMySupportTicketsQueryHandler handler = new(dbContext);

        Result<MySupportTicketsPage> result =
            await handler.Handle(
                new GetMySupportTicketsQuery(UserId.New()),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task Detail_ReturnsTheCallersTicketWithReplies_InChronologicalOrder()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        UserId owner = UserId.New();
        SupportTicket ticket =
            await SeedTicketAsync(dbContext, owner, "Help needed", Base);

        ticket.ChangeStatus(
            SupportTicketStatus.InProgress,
            AdminId,
            Base.AddMinutes(5));

        dbContext.SupportTicketReplies.Add(
            SupportTicketReply.Create(
                ticket.Id,
                AdminId,
                "First reply.",
                Base.AddMinutes(10)));

        dbContext.SupportTicketReplies.Add(
            SupportTicketReply.Create(
                ticket.Id,
                AdminId,
                "Second reply.",
                Base.AddMinutes(20)));

        await dbContext.SaveChangesAsync();

        GetMySupportTicketByIdQueryHandler handler = new(dbContext);

        Result<MySupportTicketDetail> result =
            await handler.Handle(
                new GetMySupportTicketByIdQuery(owner, ticket.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ticket.Id.Value, result.Value.TicketId);
        Assert.Equal("Help needed", result.Value.Subject);
        Assert.Equal((int)SupportTicketStatus.InProgress, result.Value.Status);
        Assert.Equal(
            new[] { "First reply.", "Second reply." },
            result.Value.Replies.Select(reply => reply.Body));
    }

    [Fact]
    public async Task Detail_ReturnsNotFound_ForAnotherUsersTicket()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        UserId other = UserId.New();
        SupportTicket ticket =
            await SeedTicketAsync(dbContext, other, "Private", Base);

        GetMySupportTicketByIdQueryHandler handler = new(dbContext);

        Result<MySupportTicketDetail> result =
            await handler.Handle(
                new GetMySupportTicketByIdQuery(UserId.New(), ticket.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupportTicketErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Detail_ReturnsNotFound_ForAnUnknownTicket()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        GetMySupportTicketByIdQueryHandler handler = new(dbContext);

        Result<MySupportTicketDetail> result =
            await handler.Handle(
                new GetMySupportTicketByIdQuery(
                    UserId.New(),
                    new SupportTicketId(Guid.NewGuid())),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupportTicketErrors.NotFound, result.Error);
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        DbContextOptions<IdentityTestDbContext> options =
            new DbContextOptionsBuilder<IdentityTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new IdentityTestDbContext(options);
    }

    private static async Task<SupportTicket> SeedTicketAsync(
        IdentityTestDbContext dbContext,
        UserId owner,
        string subject,
        DateTime createdOnUtc)
    {
        SupportTicket ticket =
            SupportTicket.Create(
                owner,
                subject,
                "I need assistance with my account.",
                createdOnUtc);

        dbContext.SupportTickets.Add(ticket);

        await dbContext.SaveChangesAsync();

        return ticket;
    }
}
