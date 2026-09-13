using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class VisibilityPreferences : ValueObject
{
    private VisibilityPreferences()
    {
    }

    private VisibilityPreferences(
        bool showProfile,
        bool showRating,
        bool showPhone,
        bool shareLocation)
    {
        ShowProfile = showProfile;
        ShowRating = showRating;
        ShowPhone = showPhone;
        ShareLocation = shareLocation;
    }

    public bool ShowProfile { get; private set; } = true;

    public bool ShowRating { get; private set; } = true;

    public bool ShowPhone { get; private set; } = false;

    public bool ShareLocation { get; private set; } = false;

    public static VisibilityPreferences Create(
        bool showProfile = true,
        bool showRating = true,
        bool showPhone = false,
        bool shareLocation = false)
    {
        return new VisibilityPreferences(
            showProfile,
            showRating,
            showPhone,
            shareLocation);
    }

    public static VisibilityPreferences Default => Create();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ShowProfile;
        yield return ShowRating;
        yield return ShowPhone;
        yield return ShareLocation;
    }
}
