using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class ServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_ShouldRespectInitialActiveStatus(
        bool isActive)
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive);

        Assert.NotEqual(ServiceId.Empty, service.Id);
        Assert.Equal("تمريض منزلي", service.ArabicName);
        Assert.Equal("Home Nursing", service.EnglishName);
        Assert.Equal(
            CaregiverType.Medical,
            service.CaregiverType);
        Assert.Equal(isActive, service.IsActive);
        Assert.Equal(
            service.CreatedOnUtc,
            service.UpdatedOnUtc);
    }

    [Fact]
    public void Create_ShouldTrimServiceNames()
    {
        Service service = Service.Create(
            "  تمريض منزلي  ",
            "  Home Nursing  ",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive: true);

        Assert.Equal("تمريض منزلي", service.ArabicName);
        Assert.Equal("Home Nursing", service.EnglishName);
    }

    [Theory]
    [InlineData(null, "Home Nursing")]
    [InlineData("", "Home Nursing")]
    [InlineData("   ", "Home Nursing")]
    [InlineData("تمريض منزلي", null)]
    [InlineData("تمريض منزلي", "")]
    [InlineData("تمريض منزلي", "   ")]
    public void Create_ShouldRejectMissingServiceName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => Service.Create(
                arabicName!,
                englishName!,
                "icons/care-service.svg",
                CaregiverType.Medical,
                isActive: true));
    }

    [Fact]
    public void Create_ShouldRejectArabicNameThatIsTooLong()
    {
        string longArabicName = new(
            'أ',
            Service.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Service.Create(
                longArabicName,
                "Home Nursing",
                "icons/care-service.svg",
                CaregiverType.Medical,
                isActive: true));
    }

    [Fact]
    public void Create_ShouldRejectEnglishNameThatIsTooLong()
    {
        string longEnglishName = new(
            'A',
            Service.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Service.Create(
                "تمريض منزلي",
                longEnglishName,
                "icons/care-service.svg",
                CaregiverType.Medical,
                isActive: true));
    }

    [Fact]
    public void Create_ShouldRejectInvalidCaregiverType()
    {
        Assert.Throws<DomainException>(
            () => Service.Create(
                "تمريض منزلي",
                "Home Nursing",
                "icons/care-service.svg",
                (CaregiverType)999,
                isActive: true));
    }

    [Fact]
    public void UpdateNames_ShouldUpdateAndTrimNames()
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive: true);

        ServiceId originalId = service.Id;
        CaregiverType originalType = service.CaregiverType;

        service.UpdateNames(
            "  رعاية تمريضية منزلية  ",
            "  Home Nursing Care  ");

        Assert.Equal(originalId, service.Id);
        Assert.Equal(
            "رعاية تمريضية منزلية",
            service.ArabicName);
        Assert.Equal(
            "Home Nursing Care",
            service.EnglishName);
        Assert.Equal(
            originalType,
            service.CaregiverType);
        Assert.True(service.IsActive);
        Assert.True(
            service.UpdatedOnUtc >= service.CreatedOnUtc);
    }

    [Fact]
    public void UpdateNames_ShouldNotPartiallyUpdate_WhenEnglishNameIsInvalid()
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive: true);

        DateTime originalUpdatedOnUtc =
            service.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => service.UpdateNames(
                "رعاية تمريضية منزلية",
                ""));

        Assert.Equal(
            "تمريض منزلي",
            service.ArabicName);

        Assert.Equal(
            "Home Nursing",
            service.EnglishName);

        Assert.Equal(
            CaregiverType.Medical,
            service.CaregiverType);

        Assert.True(service.IsActive);

        Assert.Equal(
            originalUpdatedOnUtc,
            service.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateNames_ShouldRejectInvalidName()
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive: true);

        Assert.Throws<DomainException>(
            () => service.UpdateNames(
                "",
                "Updated Home Nursing"));
    }

    [Fact]
    public void Deactivate_ShouldMakeServiceInactive()
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive: true);

        service.Deactivate();

        Assert.False(service.IsActive);
        Assert.True(
            service.UpdatedOnUtc >= service.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldMakeServiceActive()
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive: false);

        service.Activate();

        Assert.True(service.IsActive);
        Assert.True(
            service.UpdatedOnUtc >= service.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldDoNothing_WhenAlreadyActive()
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive: true);

        DateTime updatedOnUtc = service.UpdatedOnUtc;

        service.Activate();

        Assert.True(service.IsActive);
        Assert.Equal(
            updatedOnUtc,
            service.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldDoNothing_WhenAlreadyInactive()
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/care-service.svg",
            CaregiverType.Medical,
            isActive: false);

        DateTime updatedOnUtc = service.UpdatedOnUtc;

        service.Deactivate();

        Assert.False(service.IsActive);
        Assert.Equal(
            updatedOnUtc,
            service.UpdatedOnUtc);
    }

    [Fact]
public void Create_ShouldStoreTrimmedIconPath()
{
    Service service = Service.Create(
        "تمريض منزلي",
        "Home Nursing",
        "  icons/home-nursing.svg  ",
        CaregiverType.Medical,
        isActive: true);

    Assert.Equal(
        "icons/home-nursing.svg",
        service.IconPath);
}

[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
public void Create_ShouldRejectMissingIcon(
    string? iconPath)
{
    Assert.Throws<DomainException>(
        () => Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            iconPath!,
            CaregiverType.Medical,
            isActive: true));
}

[Fact]
public void Create_ShouldRejectLongIconPath()
{
    string longIconPath = new(
        'A',
        Service.MaximumIconPathLength + 1);

    Assert.Throws<DomainException>(
        () => Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            longIconPath,
            CaregiverType.Medical,
            isActive: true));
}

[Fact]
public void UpdateIcon_ShouldReplaceAndTrimIconPath()
{
    Service service = Service.Create(
        "تمريض منزلي",
        "Home Nursing",
        "icons/old.svg",
        CaregiverType.Medical,
        isActive: true);

    ServiceId originalId = service.Id;
    CaregiverType originalType =
        service.CaregiverType;

    service.UpdateIcon(
        "  icons/new.svg  ");

    Assert.Equal(originalId, service.Id);
    Assert.Equal(originalType, service.CaregiverType);
    Assert.Equal("icons/new.svg", service.IconPath);
    Assert.Equal("تمريض منزلي", service.ArabicName);
    Assert.Equal("Home Nursing", service.EnglishName);
    Assert.True(service.IsActive);
}

[Fact]
public void UpdateIcon_ShouldRejectInvalidPathWithoutMutation()
{
    Service service = Service.Create(
        "تمريض منزلي",
        "Home Nursing",
        "icons/original.svg",
        CaregiverType.Medical,
        isActive: true);

    DateTime originalUpdatedOnUtc =
        service.UpdatedOnUtc;

    Assert.Throws<DomainException>(
        () => service.UpdateIcon(""));

    Assert.Equal(
        "icons/original.svg",
        service.IconPath);

    Assert.Equal(
        originalUpdatedOnUtc,
        service.UpdatedOnUtc);
}

    [Fact]
    public void UpdateIcon_ShouldDoNothing_WhenPathIsUnchanged()
    {
        Service service = Service.Create(
            "تمريض منزلي",
            "Home Nursing",
            "icons/home-nursing.svg",
            CaregiverType.Medical,
            isActive: true);

        DateTime originalUpdatedOnUtc =
            service.UpdatedOnUtc;

        service.UpdateIcon(
            "  icons/home-nursing.svg  ");

        Assert.Equal(
            originalUpdatedOnUtc,
            service.UpdatedOnUtc);
    }
}