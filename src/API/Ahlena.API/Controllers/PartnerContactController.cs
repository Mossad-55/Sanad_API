using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.ProblemDetail;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Support;

namespace Ahlena.API.Controllers;

/// <summary>
/// The body of a partner contact request. Every field is the submitter's own
/// request data; there is no user, no account and no faked identity.
/// <para>
/// The three genuinely required fields (<c>Name</c>, <c>Email</c>,
/// <c>Message</c>) come first so the generated schema marks exactly those as
/// required; the optional fields carry <c>null</c> defaults, which keeps them
/// out of the schema's <c>required</c> array. Wire names and casing are
/// unchanged.
/// </para>
/// </summary>
public sealed record SubmitPartnerContactRequest(
    string Name,
    string Email,
    string Message,
    string? Company = null,
    string? Subject = null,
    string? Phone = null,
    string? CompanyType = null,
    string? City = null);

/// <summary>
/// The anonymous partner/business contact surface. Its destination is the
/// existing admin support inbox: the submission is persisted by the
/// application layer as its own aggregate and read by the admin support
/// surface. No account is created for the submitter.
/// <para>
/// Anti-spam is minimal and reuses the existing conventions: the caller sends
/// an <c>Idempotency-Key</c> header (a retry returns the same stored
/// contact, a reused key with different content is a conflict) and the
/// application layer applies the per-client submission window.
/// </para>
/// </summary>
[AllowAnonymous]
[Route("api/v1/partner-contact")]
public sealed class PartnerContactController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public PartnerContactController(
        ISender sender)
    {
        _sender = sender;
    }

    /// <param name="idempotencyKey">
    /// The caller-supplied <c>Idempotency-Key</c> header. It is required: the
    /// same key with the same content returns the stored contact instead of
    /// creating a second row, and the same key with different content is
    /// <c>409 Identity.PartnerContact.IdempotencyConflict</c>.
    /// </param>
    [HttpPost]
    [ProducesResponseType(
        typeof(PartnerContactResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SubmitPartnerContact(
        [FromBody] SubmitPartnerContactRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new SubmitPartnerContactCommand(
                    request.Name,
                    request.Company,
                    request.Email,
                    request.Subject,
                    request.Message,
                    idempotencyKey ?? string.Empty,
                    ClientKey(),
                    request.Phone,
                    request.CompanyType,
                    request.City),
                cancellationToken);

        return ToPartnerContactActionResult(result);
    }

    /// <summary>
    /// The caller-scoped anti-spam key: a one-way digest of the request's
    /// remote address. It is derived by the server, never supplied by the
    /// client, and it is never returned by any read.
    /// </summary>
    private string ClientKey()
    {
        string address =
            HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(address)));
    }

    /// <summary>
    /// The idempotency conflict and the rate-limit answer keep their own
    /// status and code; the rate limit also carries the retry delay in the
    /// <c>Retry-After</c> header, like the other rate-limited surfaces.
    /// </summary>
    private IActionResult ToPartnerContactActionResult(
        Result<PartnerContactResponse> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        if (result.Error.Code == PartnerContactErrors.RateLimitedCode)
        {
            return RateLimited(result.Error);
        }

        return ToActionResult(result);
    }

    private IActionResult RateLimited(Error error)
    {
        ProblemDetails problemDetails =
            ResultProblemDetailsMapper.Create(error, HttpContext);

        problemDetails.Type = "https://httpstatuses.com/429";
        problemDetails.Title = "Too Many Requests";
        problemDetails.Status = StatusCodes.Status429TooManyRequests;
        problemDetails.Detail = "The rate limit was reached. Retry after the indicated delay.";

        if (error.RetryAfter is { } retryAfter)
        {
            Response.Headers.RetryAfter = Math
                .Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            problemDetails);
    }
}
