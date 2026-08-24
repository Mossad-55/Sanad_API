using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class LinkExternalLoginCommandHandler :
    ICommandHandler<
        LinkExternalLoginCommand,
        LinkExternalLoginResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IExternalIdentityVerifier _externalIdentityVerifier;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LinkExternalLoginCommandHandler(
        IIdentityDbContext dbContext,
        IExternalIdentityVerifier externalIdentityVerifier,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _externalIdentityVerifier = externalIdentityVerifier;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LinkExternalLoginResponse>> Handle(
        LinkExternalLoginCommand request,
        CancellationToken cancellationToken)
    {
        VerifiedExternalIdentity? externalIdentity =
            await _externalIdentityVerifier.VerifyAsync(
                request.Provider,
                new ExternalIdentityCredential(
                    request.ProviderCredential,
                    request.Nonce),
                cancellationToken);

        if (!TryNormalizeExternalIdentity(
            request.Provider,
            externalIdentity,
            out string providerSubject))
        {
            return SocialLoginErrors.ExternalLinkFailed;
        }

        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.UserId,
                    cancellationToken);

        if (user is null ||
            !IsEligibleNonElderlyUser(user) ||
            user.Status is
                UserStatus.Suspended or
                UserStatus.Blocked)
        {
            return SocialLoginErrors.ExternalLinkFailed;
        }

        bool providerSubjectExists =
            await _dbContext.Users.AnyAsync(
                item =>
                    item.ExternalLogins.Any(
                        externalLogin =>
                            externalLogin.Provider ==
                                request.Provider &&
                            externalLogin.ProviderSubject ==
                                providerSubject),
                cancellationToken);

        if (providerSubjectExists ||
            user.ExternalLogins.Any(
                externalLogin =>
                    externalLogin.Provider ==
                    request.Provider))
        {
            return SocialLoginErrors.ExternalLinkFailed;
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        user.LinkExternalLogin(
            request.Provider,
            providerSubject,
            utcNow);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new LinkExternalLoginResponse(
            user.Id,
            request.Provider,
            utcNow);
    }

    private static bool IsEligibleNonElderlyUser(
        User user)
    {
        return user.Accounts.Any(
            account =>
                account.AccountType !=
                AccountType.Elderly);
    }

    private static bool TryNormalizeExternalIdentity(
        ExternalLoginProvider requestedProvider,
        VerifiedExternalIdentity? externalIdentity,
        out string providerSubject)
    {
        providerSubject = string.Empty;

        if (externalIdentity is null ||
            externalIdentity.Provider !=
                requestedProvider ||
            string.IsNullOrWhiteSpace(
                externalIdentity.ProviderSubject))
        {
            return false;
        }

        providerSubject =
            externalIdentity.ProviderSubject.Trim();

        return providerSubject.Length <=
            UserExternalLogin
                .MaximumProviderSubjectLength;
    }
}