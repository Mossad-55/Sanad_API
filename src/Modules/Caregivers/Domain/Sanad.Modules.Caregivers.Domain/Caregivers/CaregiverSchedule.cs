using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverSchedule : ValueObject
{
    private readonly List<CaregiverAvailabilitySlot> _slots = [];

    private CaregiverSchedule()
    {
    }

    public IReadOnlyCollection<CaregiverAvailabilitySlot> Slots
        => _slots.AsReadOnly();

    public static CaregiverSchedule Create()
    {
        return new CaregiverSchedule();
    }

    public void AddSlot(
        CaregiverAvailabilitySlot slot)
    {
        bool alreadyExists =
            _slots.Any(x => x.DayOfWeek == slot.DayOfWeek);

        if (alreadyExists)
        {
            throw new DomainException(
                "A slot already exists for this day.");
        }

        _slots.Add(slot);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (CaregiverAvailabilitySlot slot in _slots)
        {
            yield return slot;
        }
    }
}