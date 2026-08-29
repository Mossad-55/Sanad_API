using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class ProfessionalTitleLookupsTests
{
    [Fact]
    public async Task Create_ShouldPersistTitleAsActive()
    {
        using var db = CreateDbContext();
        var result = await new CreateProfessionalTitleCommandHandler(db).Handle(
            new CreateProfessionalTitleCommand("أخصائي", "Specialist", true), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateName()
    {
        using var db = CreateDbContext();
        var handler = new CreateProfessionalTitleCommandHandler(db);
        await handler.Handle(new CreateProfessionalTitleCommand("أخصائي", "Specialist", true), default);

        var duplicate = await handler.Handle(
            new CreateProfessionalTitleCommand("أخصائي", "Specialist", true), default);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(LookupsErrors.NameAlreadyInUse, duplicate.Error);
    }

    [Fact]
    public async Task GetAll_ShouldReturnActiveAndInactive()
    {
        using var db = CreateDbContext();
        var created = await new CreateProfessionalTitleCommandHandler(db).Handle(
            new CreateProfessionalTitleCommand("أخصائي", "Specialist", true), default);
        await new SetProfessionalTitleActiveCommandHandler(db).Handle(
            new SetProfessionalTitleActiveCommand(created.Value.Id, false), default);

        var result = await new GetAllProfessionalTitlesQueryHandler(db).Handle(
            new GetAllProfessionalTitlesQuery(), default);

        Assert.Single(result.Value);
        Assert.False(result.Value[0].IsActive);
    }

    private static CaregiversDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new CaregiversDbContext(options);
    }
}