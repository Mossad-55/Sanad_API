using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Elderlies;

namespace Sanad.UnitTests.API;

public sealed class FamilyEmergencyContactControllerTests
{
    [Fact]
    public async Task GetEmergencyContact_WhenUnset_ReturnsOkWithJsonNull()
    {
        var sender = new NullEmergencyContactSender();
        var controller = new FamilyController(sender, fileStorage: null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())
                    ], "test"))
                }
            }
        };

        IActionResult result = await controller.GetEmergencyContact(
            Guid.NewGuid(), CancellationToken.None);

        JsonResult json = Assert.IsType<JsonResult>(result);
        Assert.Equal(StatusCodes.Status200OK, json.StatusCode);
        Assert.Null(json.Value);
        Assert.IsType<GetFamilyEmergencyContactQuery>(sender.LastRequest);
    }

    private sealed class NullEmergencyContactSender : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            object response = request is GetFamilyEmergencyContactQuery
                ? Result<EmergencyContactResponse?>.Success(null)
                : throw new NotSupportedException(request.GetType().Name);
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
