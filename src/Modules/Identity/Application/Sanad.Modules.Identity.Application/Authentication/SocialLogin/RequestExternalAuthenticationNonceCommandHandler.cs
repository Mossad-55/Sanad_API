using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Security;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class RequestExternalAuthenticationNonceCommandHandler :
    ICommandHandler<
        RequestExternalAuthenticationNonceCommand,
        RequestExternalAuthenticationNonceResponse>
{
    private readonly IExternalAuthenticationNonceStore
        _nonceStore;

    private readonly IDateTimeProvider
        _dateTimeProvider;

    public RequestExternalAuthenticationNonceCommandHandler(
        IExternalAuthenticationNonceStore nonceStore,
        IDateTimeProvider dateTimeProvider)
    {
        _nonceStore =
            nonceStore;

        _dateTimeProvider =
            dateTimeProvider;
    }

    public async Task<
        Result<RequestExternalAuthenticationNonceResponse>> Handle(
            RequestExternalAuthenticationNonceCommand request,
            CancellationToken cancellationToken)
    {
        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        DateTime expiresOnUtc =
            utcNow.Add(
                ExternalAuthenticationNoncePolicy.Lifetime);

        string nonce =
            await _nonceStore.CreateAsync(
                request.Provider,
                utcNow,
                expiresOnUtc,
                cancellationToken);

        return new RequestExternalAuthenticationNonceResponse(
            nonce,
            expiresOnUtc);
    }
}