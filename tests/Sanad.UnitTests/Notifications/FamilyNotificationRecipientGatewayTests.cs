using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

public sealed class FamilyNotificationRecipientGatewayTests
{
    [Fact]
    public async Task Gateway_ShouldScopeToActiveFamilyMembersAndDeduplicateResults()
    {
        await using var db = CreateDb();
        var owner = UserId.New(); var member = UserId.New(); var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "target");
        family.AddMember(FamilyMember.Create(member, owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father, FullName.Create("عمر"), FullName.Create("Elderly"), Gender.Male, new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        db.Families.Add(family); db.Elderlies.Add(elderly); await db.SaveChangesAsync();

        var sender = new RecipientSender();
        var gateway = new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(db, sender);
        var result = await Invoke(gateway, elderlyIdentity, elderly.Id.Value);

        Assert.Equal(new[] { owner, member }, result);
        Assert.Equal(new[] { owner, member }, sender.LastIds);
    }

    [Fact]
    public async Task Gateway_ShouldReturnNoRecipientsForDeletedFamilyOrMismatchedElderly()
    {
        await using var db = CreateDb();
        var owner = UserId.New(); var elderlyIdentity = UserId.New();
        var family = Family.Create(owner); family.MarkDeleted("requested", null);
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father, FullName.Create("عمر"), FullName.Create("Elderly"), Gender.Male, new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        db.Families.Add(family); db.Elderlies.Add(elderly); await db.SaveChangesAsync();
        var sender = new RecipientSender();
        var gateway = new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(db, sender);
        Assert.Empty(await Invoke(gateway, elderlyIdentity, elderly.Id.Value));
        Assert.Empty(sender.LastIds!);
        Assert.Empty(await Invoke(gateway, UserId.New(), elderly.Id.Value));
    }

    private static async Task<IReadOnlyList<UserId>> Invoke(object gateway, UserId elderlyIdentity, Guid elderlyId)
    {
        var recipientType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Sanad.Modules.Notifications.Application.Abstractions.Recipients.ElderlyRecipient")).First(type => type is not null)!;
        var recipient = Activator.CreateInstance(recipientType, elderlyIdentity, elderlyId)!;
        var task = (Task)gateway.GetType().GetMethod("GetCheckInAlertRecipientsAsync")!.Invoke(gateway, [recipient, CancellationToken.None])!;
        await task;
        return (IReadOnlyList<UserId>)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class RecipientSender : ISender
    {
        public IReadOnlyList<UserId>? LastIds { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var ids = (IReadOnlyCollection<UserId>)request.GetType().GetProperty("UserIds")!.GetValue(request)!;
            LastIds = ids.ToArray();
            var values = LastIds.Concat(LastIds.Take(1)).ToArray();
            var resultType = typeof(Result<>).MakeGenericType(typeof(IReadOnlyList<UserId>));
            var result = resultType.GetMethod("Success", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [values]);
            return Task.FromResult((TResponse)result!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
