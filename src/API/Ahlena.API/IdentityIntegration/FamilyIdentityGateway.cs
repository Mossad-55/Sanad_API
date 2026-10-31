using MediatR;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Identity;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Application.Users;

namespace Ahlena.API.IdentityIntegration;

public sealed class FamilyIdentityGateway : IFamilyIdentityGateway
{
    private readonly ISender _sender;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;

    public FamilyIdentityGateway(
        ISender sender,
        IEmailSender emailSender,
        IConfiguration configuration)
    {
        _sender = sender;
        _emailSender = emailSender;
        _configuration = configuration;
    }

    public async Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new GetElderlyUserIdByPhoneQuery(phoneNumber),
                cancellationToken);

        if (result.IsFailure)
        {
            return Result<ElderlyIdentityAccount>.Failure(
                result.Error);
        }

        return new ElderlyIdentityAccount(
            result.Value.UserId,
            Exists: true,
            IsElderly: result.Value.IsElderly);
    }

    public async Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(
        string arabicFullName,
        string englishFullName,
        string phoneNumber,
        Gender gender,
        DateOnly dateOfBirth,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new CreateElderlyIdentityCommand(
                    arabicFullName,
                    englishFullName,
                    phoneNumber,
                    gender,
                    dateOfBirth,
                    utcNow),
                cancellationToken);

        if (result.IsFailure)
        {
            return Result<ElderlyIdentityAccount>.Failure(
                result.Error);
        }

        return new ElderlyIdentityAccount(
            result.Value.UserId,
            Exists: true,
            IsElderly: true);
    }

    public async Task DeleteElderlyAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        await _sender.Send(
            new DeleteIdentityUserCommand(userId),
            cancellationToken);
    }

    public async Task<Result<FamilyInviteeAccount>> GetFamilyInviteeByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new GetFamilyInviteeByEmailQuery(email),
                cancellationToken);

        if (result.IsFailure)
        {
            return Result<FamilyInviteeAccount>.Failure(
                result.Error);
        }

        return new FamilyInviteeAccount(
            result.Value.UserId,
            Exists: true,
            HasFamilyAccount: result.Value.HasFamilyAccount);
    }

    public async Task<IReadOnlyList<FamilyMemberProfile>>
        GetFamilyMemberProfilesAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default)
    {
        var result =
            await _sender.Send(
                new GetUserProfilesByIdsQuery(userIds),
                cancellationToken);

        if (result.IsFailure)
        {
            return [];
        }

        return result.Value
            .Select(profile => new FamilyMemberProfile(
                profile.UserId,
                profile.ArabicFullName,
                profile.EnglishFullName,
                profile.Email,
                profile.PhoneNumber,
                // Avatar parity (Owner lock #34): no authorized cross-user avatar
                // route exists for family members (GET /auth/avatar serves only
                // the caller's own avatar), so the raw key is dropped here and
                // the field reads null on the wire. A general identity-avatar
                // route remains a separate Owner decision.
                null,
                profile.Status,
                profile.LastLoginOnUtc))
            .ToList();
    }

    public async Task SendFamilyInvitationEmailAsync(
        string email,
        string familyName,
        string invitationToken,
        CancellationToken cancellationToken = default)
    {
        string? inviteBaseUrl =
            _configuration["App:InviteBaseUrl"];

        if (string.IsNullOrWhiteSpace(inviteBaseUrl))
        {
            throw new InvalidOperationException(
                "App:InviteBaseUrl configuration is required.");
        }

        string inviteLink =
            $"{inviteBaseUrl.TrimEnd('/')}?token=" +
            Uri.EscapeDataString(invitationToken);

        await _emailSender.SendFamilyInvitationAsync(
            email,
            familyName,
            inviteLink,
            cancellationToken);
    }

    public async Task<Result> AnonymizeFamilyAccountsAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken = default)
    {
        return await _sender.Send(
            new AnonymizeFamilyAccountsCommand(userIds),
            cancellationToken);
    }
}