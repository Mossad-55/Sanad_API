using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Elderlies.Medical;

public sealed record MedicalHistoryEntry(
    int? Year,
    string Title,
    string? Description)
{
    public const int MaximumTitleLength = 200;
    public const int MaximumDescriptionLength = 1000;

    public static MedicalHistoryEntry Create(
        int? year,
        string title,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Medical history title is required.");
        }

        if (year.HasValue && (year < 1900 || year > 2100))
        {
            throw new DomainException("Medical history year must be between 1900 and 2100.");
        }

        string trimmedTitle = title.Trim();
        if (trimmedTitle.Length > MaximumTitleLength)
        {
            throw new DomainException(
                $"Medical history title cannot exceed {MaximumTitleLength} characters.");
        }

        string? trimmedDesc = null;
        if (!string.IsNullOrWhiteSpace(description))
        {
            trimmedDesc = description.Trim();
            if (trimmedDesc.Length > MaximumDescriptionLength)
            {
                throw new DomainException(
                    $"Medical history description cannot exceed {MaximumDescriptionLength} characters.");
            }
        }

        return new MedicalHistoryEntry(year, trimmedTitle, trimmedDesc);
    }
}