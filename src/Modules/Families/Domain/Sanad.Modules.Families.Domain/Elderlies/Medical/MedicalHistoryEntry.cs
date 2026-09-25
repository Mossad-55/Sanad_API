using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Elderlies.Medical;

public sealed record MedicalHistoryEntry(
    int? Year,
    string Title,
    string? Description,
    DateOnly? ProcedureDate = null)
{
    public const int MaximumTitleLength = 200;
    public const int MaximumDescriptionLength = 1000;

    public static MedicalHistoryEntry Create(
        int? year,
        string title,
        string? description = null,
        DateOnly? procedureDate = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Medical history title is required.");
        }

        if (year.HasValue && (year < 1900 || year > 2100))
        {
            throw new DomainException("Medical history year must be between 1900 and 2100.");
        }

        if (procedureDate.HasValue &&
            (procedureDate.Value.Year < 1900 || procedureDate.Value.Year > 2100))
        {
            throw new DomainException("Medical history procedure date must be between 1900 and 2100.");
        }

        if (year.HasValue && procedureDate.HasValue && year.Value != procedureDate.Value.Year)
        {
            throw new DomainException("Medical history year must match the procedure date year.");
        }

        int? effectiveYear = procedureDate?.Year ?? year;

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

        return new MedicalHistoryEntry(effectiveYear, trimmedTitle, trimmedDesc, procedureDate);
    }
}
