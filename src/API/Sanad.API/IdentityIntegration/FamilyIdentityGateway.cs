using MediatR;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.API.IdentityIntegration;

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
}