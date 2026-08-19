using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Selections;

public sealed class CaregiverLanguageSelection : Entity<LanguageId>
{
    private CaregiverLanguageSelection()
    {
    }

    private CaregiverLanguageSelection(LanguageId languageId)
        : base(languageId)
    {
    }

    internal static CaregiverLanguageSelection Create(LanguageId languageId)
    {
        if (languageId == LanguageId.Empty)
        {
            throw new DomainException(
                "Language ID is required.");
        }

        return new CaregiverLanguageSelection(languageId);
    }
}