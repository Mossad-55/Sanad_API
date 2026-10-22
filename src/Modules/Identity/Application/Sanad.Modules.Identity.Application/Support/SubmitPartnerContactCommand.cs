using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Support;

/// <summary>
/// A partner/business contact request submitted from the public contact form.
/// The submitter's own details are **request data**: no account is created and
/// no identity is faked.
/// <para>
/// <c>IdempotencyKey</c> is the caller-supplied <c>Idempotency-Key</c> header
/// value; <c>ClientKey</c> is the caller-scoped anti-spam key the API layer
/// derives from the request (never supplied by the client). Both are
/// anti-spam inputs, not contact data.
/// </para>
/// </summary>
public sealed record SubmitPartnerContactCommand(
    string Name,
    string? Company,
    string Email,
    string? Subject,
    string Message,
    string IdempotencyKey,
    string ClientKey)
    : ICommand<PartnerContactResponse>;

/// <summary>
/// The receipt of an accepted partner contact. It carries the stored contact
/// id and the received timestamp only — the submitted contact data is not
/// echoed back.
/// </summary>
public sealed record PartnerContactResponse(
    PartnerContactId ContactId,
    DateTime SubmittedOnUtc);
