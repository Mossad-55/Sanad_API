using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Medications;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class MedicationTests
{
    [Fact]
    public void Create_WithValidParameters_SetsPropertiesAndActiveStatus()
    {
        var elderlyId = ElderlyId.New();
        var userId = UserId.New();
        var times = new[] { new TimeOnly(8, 0), new TimeOnly(20, 0) };

        var med = Medication.Create(
            elderlyId,
            userId,
            "Panadol Extra",
            "500 mg",
            "قرص",
            1,
            times,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            "بعد الأكل",
            stockQuantity: 30,
            lowStockThreshold: 5);

        Assert.Equal("Panadol Extra", med.Name);
        Assert.Equal("500 mg", med.Dosage);
        Assert.Equal("قرص", med.DoseUnit);
        Assert.Equal(1, med.DoseQuantity);
        Assert.Equal(2, med.DoseTimes.Count);
        Assert.Equal(MedicationStatus.Active, med.Status);
        Assert.Equal(StockStatus.Normal, med.GetStockStatus());
    }

    [Fact]
    public void DecrementStock_WhenStockDropsBelowThreshold_UpdatesStockStatusToLowStock()
    {
        var med = Medication.Create(
            ElderlyId.New(),
            UserId.New(),
            "Metformin",
            "500 mg",
            "قرص",
            1,
            new[] { new TimeOnly(9, 0) },
            new DateOnly(2026, 9, 1),
            null,
            stockQuantity: 6,
            lowStockThreshold: 5);

        Assert.Equal(StockStatus.Normal, med.GetStockStatus());

        med.DecrementStock(2);

        Assert.Equal(4, med.StockQuantity);
        Assert.Equal(StockStatus.LowStock, med.GetStockStatus());

        med.DecrementStock(4);

        Assert.Equal(0, med.StockQuantity);
        Assert.Equal(StockStatus.OutOfStock, med.GetStockStatus());
    }

    [Fact]
    public void Create_WithoutTimes_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Medication.Create(
                ElderlyId.New(),
                UserId.New(),
                "Aspirin",
                "81 mg",
                "قرص",
                1,
                Array.Empty<TimeOnly>(),
                new DateOnly(2026, 9, 1),
                null));
    }

    [Fact]
    public void Create_EndDateBeforeStartDate_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Medication.Create(
                ElderlyId.New(),
                UserId.New(),
                "Aspirin",
                "81 mg",
                "قرص",
                1,
                new[] { new TimeOnly(8, 0) },
                new DateOnly(2026, 9, 10),
                new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void DoseLog_MarkAsTaken_UpdatesStatusAndTimestamp()
    {
        var dose = MedicationDoseLog.CreateScheduled(
            MedicationId.New(),
            ElderlyId.New(),
            new DateOnly(2026, 9, 2),
            new TimeOnly(8, 0));

        var userId = UserId.New();
        var now = DateTime.UtcNow;

        dose.MarkAsTaken(userId, now, "تم التناول مع الإفطار");

        Assert.Equal(DoseStatus.Taken, dose.Status);
        Assert.Equal(now, dose.TakenAtUtc);
        Assert.Equal(userId, dose.LoggedByUserId);
        Assert.Equal("تم التناول مع الإفطار", dose.Notes);
    }
}