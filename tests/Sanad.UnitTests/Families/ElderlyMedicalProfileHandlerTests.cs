using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.Medical;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class ElderlyMedicalProfileHandlerTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    [Fact]
    public async Task GetMedicalProfile_ShouldReturnDefault_WhenNotYetSet()
    {
        using var dbContext = CreateDbContext();
        var userId = UserId.New();
        var family = Family.Create(userId, "Family");
        dbContext.Families.Add(family);

        var elderly = Elderly.Create(
            userId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("الأب"),
            FullName.Create("Father"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 9, 2));

        dbContext.Elderlies.Add(elderly);
        await dbContext.SaveChangesAsync();

        var handler = new GetElderlyMedicalProfileQueryHandler(dbContext);
        var result = await handler.Handle(
            new GetElderlyMedicalProfileQuery(userId, elderly.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BloodType.Unknown, result.Value.BloodType);
        Assert.Null(result.Value.HeightCm);
        Assert.Empty(result.Value.ChronicConditions);
    }

    [Fact]
    public async Task UpdateMedicalProfile_ShouldPersistAndReturnUpdatedProfile()
    {
        using var dbContext = CreateDbContext();
        var userId = UserId.New();
        var family = Family.Create(userId, "Family");
        dbContext.Families.Add(family);

        var elderly = Elderly.Create(
            userId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Mother,
            FullName.Create("الأم"),
            FullName.Create("Mother"),
            Gender.Female,
            new DateOnly(1955, 1, 1),
            new DateOnly(2026, 9, 2));

        dbContext.Elderlies.Add(elderly);
        await dbContext.SaveChangesAsync();

        var handler = new UpdateElderlyMedicalProfileCommandHandler(dbContext);
        var command = new UpdateElderlyMedicalProfileCommand(
            userId,
            elderly.Id,
            BloodType.APositive,
            160,
            65.0m,
            ["Hypertension"],
            [new AllergyDto(AllergyCategory.Drug, "Aspirin", "Stomach ache")],
            [new MedicalHistoryDto(2018, "Gallbladder Removal", null)]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BloodType.APositive, result.Value.BloodType);
        Assert.Equal(160, result.Value.HeightCm);
        Assert.Equal(65.0m, result.Value.WeightKg);
        Assert.Single(result.Value.ChronicConditions);
        Assert.Single(result.Value.Allergies);
        Assert.Single(result.Value.MedicalHistory);
    }
}