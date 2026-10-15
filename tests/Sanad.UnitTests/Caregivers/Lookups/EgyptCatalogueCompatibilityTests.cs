using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Lookups;

// Real validators/handlers over every candidate record, isolated EF InMemory.
// Not a PostgreSQL, HTTP, importer, or geographical/linguistic accuracy proof.
public sealed class EgyptCatalogueCompatibilityTests
{
    [Theory]
    [InlineData("EG01")]
    [InlineData("EG02")]
    [InlineData("EG03")]
    [InlineData("EG04")]
    [InlineData("EG11")]
    [InlineData("EG12")]
    [InlineData("EG13")]
    [InlineData("EG14")]
    [InlineData("EG15")]
    [InlineData("EG16")]
    [InlineData("EG17")]
    [InlineData("EG18")]
    [InlineData("EG19")]
    [InlineData("EG21")]
    [InlineData("EG22")]
    [InlineData("EG23")]
    [InlineData("EG24")]
    [InlineData("EG25")]
    [InlineData("EG26")]
    [InlineData("EG27")]
    [InlineData("EG28")]
    [InlineData("EG29")]
    [InlineData("EG31")]
    [InlineData("EG32")]
    [InlineData("EG33")]
    [InlineData("EG34")]
    [InlineData("EG35")]
    public async Task EntireBranch_ShouldValidatePersistAndReadBackThroughRealLookupHandlers(string governorateKey)
    {
        using var catalogue = ReadCatalogue();
        var root = catalogue.RootElement;
        Assert.Equal(27, root.GetProperty("governorates").GetArrayLength());
        Assert.Equal(347, root.GetProperty("cities").GetArrayLength());
        Assert.Equal(5712, root.GetProperty("areas").GetArrayLength());
        var governorate = root.GetProperty("governorates").EnumerateArray()
            .Single(row => Text(row, "key") == governorateKey);
        var cities = root.GetProperty("cities").EnumerateArray()
            .Where(row => Text(row, "governorateKey") == governorateKey).ToArray();
        var areasByCity = root.GetProperty("areas").EnumerateArray()
            .ToLookup(row => Text(row, "cityKey"), StringComparer.Ordinal);

        // Explicit empty disposable fixture, no app configuration/host/providers.
        using var db = new CaregiversDbContext(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase($"egypt-catalogue-{Guid.NewGuid():N}").Options);
        Assert.Empty(await db.Governorates.AsNoTracking().ToListAsync());
        var governorateCommand = new CreateGovernorateCommand(
            Text(governorate, "arabicName"), Text(governorate, "englishName"));
        Assert.True((await new CreateGovernorateCommandValidator().ValidateAsync(governorateCommand)).IsValid);
        var createdGovernorate = await new CreateGovernorateCommandHandler(db).Handle(governorateCommand, default);
        Assert.True(createdGovernorate.IsSuccess, governorateKey);
        var persistedGovernorate = await db.Governorates.AsNoTracking().SingleAsync();
        Assert.Equal(createdGovernorate.Value.Id, persistedGovernorate.Id);
        Assert.Equal(Text(governorate, "arabicName"), persistedGovernorate.ArabicName);
        Assert.Equal(Text(governorate, "englishName"), persistedGovernorate.EnglishName);
        Assert.True(persistedGovernorate.IsActive);

        var expectedAreaCount = 0;
        CityId firstCityId = CityId.Empty;
        foreach (var city in cities)
        {
            var cityKey = Text(city, "key");
            var command = new CreateCityCommand(persistedGovernorate.Id,
                Text(city, "arabicName"), Text(city, "englishName"));
            Assert.True((await new CreateCityCommandValidator().ValidateAsync(command)).IsValid, cityKey);
            var createdCity = await new CreateCityCommandHandler(db).Handle(command, default);
            Assert.True(createdCity.IsSuccess, cityKey);
            var persistedCity = await db.Cities.AsNoTracking().SingleAsync(row => row.Id == createdCity.Value.Id);
            Assert.Equal(persistedGovernorate.Id, persistedCity.GovernorateId);
            Assert.True(persistedCity.IsActive);
            if (firstCityId == CityId.Empty) firstCityId = persistedCity.Id;

            var expectedNames = areasByCity[cityKey].ToDictionary(
                row => Text(row, "arabicName"), row => Text(row, "englishName"), StringComparer.Ordinal);
            Assert.NotEmpty(expectedNames);
            foreach (var area in areasByCity[cityKey])
            {
                var areaCommand = new CreateAreaCommand(persistedCity.Id,
                    Text(area, "arabicName"), Text(area, "englishName"));
                Assert.True((await new CreateAreaCommandValidator().ValidateAsync(areaCommand)).IsValid, Text(area, "key"));
                var createdArea = await new CreateAreaCommandHandler(db).Handle(areaCommand, default);
                Assert.True(createdArea.IsSuccess, Text(area, "key"));
                Assert.Equal(persistedCity.Id, createdArea.Value.CityId);
                Assert.True(createdArea.Value.IsActive);
            }
            expectedAreaCount += expectedNames.Count;
            var adminAreas = await new GetAllAreasQueryHandler(db).Handle(new GetAllAreasQuery(persistedCity.Id), default);
            var publicAreas = await new GetActiveAreasQueryHandler(db).Handle(new GetActiveAreasQuery(persistedCity.Id), default);
            Assert.True(adminAreas.IsSuccess);
            Assert.True(publicAreas.IsSuccess);
            Assert.Equal(expectedNames.Count, adminAreas.Value.Count);
            Assert.Equal(expectedNames.Count, publicAreas.Value.Count);
            Assert.All(publicAreas.Value, row => Assert.Equal(expectedNames[row.ArabicName], row.EnglishName));
            Assert.All(adminAreas.Value, row => Assert.Equal(persistedCity.Id, row.CityId));
        }

        Assert.Equal(cities.Length, await db.Cities.AsNoTracking().CountAsync());
        Assert.Equal(expectedAreaCount, await db.Areas.AsNoTracking().CountAsync());
        var publicCities = await new GetActiveCitiesQueryHandler(db).Handle(
            new GetActiveCitiesQuery(persistedGovernorate.Id), default);
        Assert.True(publicCities.IsSuccess);
        Assert.Equal(cities.Length, publicCities.Value.Count);
        var expectedCities = cities.ToDictionary(row => Text(row, "arabicName"), row => Text(row, "englishName"), StringComparer.Ordinal);
        Assert.All(publicCities.Value, row => Assert.Equal(expectedCities[row.ArabicName], row.EnglishName));

        // Verify admin maintenance does not physically delete seeded references.
        var duplicate = await new CreateGovernorateCommandHandler(db).Handle(governorateCommand, default);
        Assert.True(duplicate.IsFailure);
        Assert.Equal(LookupsErrors.NameAlreadyInUse, duplicate.Error);
        var deactivated = await new SetGovernorateActiveCommandHandler(db).Handle(
            new SetGovernorateActiveCommand(persistedGovernorate.Id, false), default);
        Assert.True(deactivated.IsSuccess);
        var hidden = await new GetActiveAreasQueryHandler(db).Handle(new GetActiveAreasQuery(firstCityId), default);
        Assert.True(hidden.IsSuccess);
        Assert.Empty(hidden.Value);
        Assert.Equal(expectedAreaCount, await db.Areas.AsNoTracking().CountAsync());
        var activated = await new SetGovernorateActiveCommandHandler(db).Handle(
            new SetGovernorateActiveCommand(persistedGovernorate.Id, true), default);
        Assert.True(activated.IsSuccess);
        var restored = await new GetActiveCitiesQueryHandler(db).Handle(new GetActiveCitiesQuery(persistedGovernorate.Id), default);
        Assert.Equal(cities.Length, restored.Value.Count);
    }

    private static string Text(JsonElement row, string property) =>
        row.GetProperty(property).GetString() ?? throw new InvalidDataException($"Null catalogue {property}.");

    private static JsonDocument ReadCatalogue()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "docs", "internal", "work", "portal-api-reconciliation",
                "reference-data", "egypt", "candidate.json");
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
        }
        throw new FileNotFoundException("Run these catalogue tests from a repository checkout containing candidate.json.");
    }
}
