using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Identity.Application.Support;

/// <summary>
/// Failure codes of the anonymous partner-contact surface.
/// <see cref="Invalid"/> is the caller's own bad request (a failed domain
/// rule) and answers a 400. <see cref="Conflict"/> is the idempotency
/// conflict: the same key was already used with different content.
/// <see cref="RateLimited"/> carries the retry delay the caller must wait
/// (the existing error shape) and answers a 429.
/// </summary>
public static class PartnerContactErrors
{
    public static readonly Error Invalid =
        new("Identity.PartnerContact.Invalid", "The partner contact request is invalid.");

    public static readonly Error NotFound =
        new("Identity.PartnerContact.NotFound", "Partner contact was not found.");

    public static readonly Error Conflict =
        new("Identity.PartnerContact.IdempotencyConflict", "The idempotency key was already used for a different request.");

    /// <summary>The code of the anti-spam rate-limit answer.</summary>
    public const string RateLimitedCode = "Identity.PartnerContact.RateLimited";

    public static Error RateLimited(TimeSpan retryAfter) =>
        new(
            RateLimitedCode,
            "Too many partner contact requests were submitted. Try again later.",
            retryAfter);
}
