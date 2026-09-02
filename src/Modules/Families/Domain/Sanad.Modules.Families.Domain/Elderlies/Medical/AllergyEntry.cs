using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Elderlies.Medical;

public sealed record AllergyEntry(
    AllergyCategory Category,
    string Allergen,
    string? Reaction)
{
    public const int MaximumAllergenLength = 100;
    public const int MaximumReactionLength = 200;

    public static AllergyEntry Create(
        AllergyCategory category,
        string allergen,
        string? reaction = null)
    {
        if (string.IsNullOrWhiteSpace(allergen))
        {
            throw new DomainException("Allergen name is required.");
        }

        string trimmedAllergen = allergen.Trim();
        if (trimmedAllergen.Length > MaximumAllergenLength)
        {
            throw new DomainException(
                $"Allergen name cannot exceed {MaximumAllergenLength} characters.");
        }

        string? trimmedReaction = null;
        if (!string.IsNullOrWhiteSpace(reaction))
        {
            trimmedReaction = reaction.Trim();
            if (trimmedReaction.Length > MaximumReactionLength)
            {
                throw new DomainException(
                    $"Reaction description cannot exceed {MaximumReactionLength} characters.");
            }
        }

        return new AllergyEntry(category, trimmedAllergen, trimmedReaction);
    }
}