namespace Sanad.Modules.Cms.Application.Help;

/// <summary>
/// Admin-facing view of the one global support contact row. Each field may be
/// null on its own; a configuration that has never been written returns both
/// fields as null with a <c>200</c> rather than a not-found failure.
/// </summary>
public sealed record SupportContactResponse(
    string? SupportPhone,
    string? SupportEmail,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);
