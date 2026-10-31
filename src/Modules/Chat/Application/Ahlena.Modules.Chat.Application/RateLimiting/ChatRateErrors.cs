using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Chat.Application.RateLimiting;

/// <summary>
/// Chat Plus abuse-control failures. All rate outcomes answer HTTP 429 with
/// a `Retry-After` header (forced by the Chat controller, which reads the
/// delay from <see cref="Error.RetryAfter"/>); they never reveal account or
/// resource details.
/// </summary>
public static class ChatRateErrors
{
    public static Error Sends(TimeSpan retryAfter) =>
        new(
            "Chat.RateLimited.Sends",
            "The send rate limit was reached. Retry after the indicated delay.",
            retryAfter);

    public static Error Opens(TimeSpan retryAfter) =>
        new(
            "Chat.RateLimited.Opens",
            "The conversation-open rate limit was reached. Retry after the indicated delay.",
            retryAfter);

    public static Error Uploads(TimeSpan retryAfter) =>
        new(
            "Chat.RateLimited.Uploads",
            "The upload rate limit was reached. Retry after the indicated delay.",
            retryAfter);

    public static Error Reports(TimeSpan retryAfter) =>
        new(
            "Chat.RateLimited.Reports",
            "The report rate limit was reached. Retry after the indicated delay.",
            retryAfter);

    public static Error Sync(TimeSpan retryAfter) =>
        new(
            "Chat.RateLimited.Sync",
            "The synchronization rate limit was reached. Retry after the indicated delay.",
            retryAfter);
}
