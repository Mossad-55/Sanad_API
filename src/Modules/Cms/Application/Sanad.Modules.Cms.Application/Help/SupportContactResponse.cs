namespace Sanad.Modules.Cms.Application.Help;

/// <summary>
/// Admin-facing view of the one global support contact row.
/// </summary>
public sealed record SupportContactResponse(
    string SupportPhone,
    string SupportEmail,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);
