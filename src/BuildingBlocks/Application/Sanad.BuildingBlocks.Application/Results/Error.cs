namespace Sanad.BuildingBlocks.Application.Results;

/// <summary>
/// An application error. <see cref="Code"/> and <see cref="Message"/> are the
/// stable contract; <see cref="RetryAfter"/> and <see cref="Errors"/> are
/// additive optional detail slots.
/// <para>
/// <see cref="Errors"/> carries per-field validation detail for surfaces that
/// already document a field-level <c>errors</c> map (library content
/// validation is the first consumer). It is <c>null</c> for every error that
/// has no field detail, so problem-detail bodies without field errors stay
/// byte-identical.
/// </para>
/// </summary>
public sealed record Error(
    string Code,
    string Message,
    TimeSpan? RetryAfter = null,
    IReadOnlyDictionary<string, string[]>? Errors = null
)
{
    public static readonly Error None = 
        new(string.Empty, string.Empty);

    public static readonly Error NullValue =
        new("General.Null", "A null value was provided.");
}