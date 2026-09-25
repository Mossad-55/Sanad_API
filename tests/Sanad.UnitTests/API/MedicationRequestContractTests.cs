using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Xunit;

namespace Sanad.UnitTests.API;

public sealed class MedicationRequestContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void UpdateRequest_WhenStockIsAbsent_PreservesLegacyInventoryMode()
    {
        var request = Deserialize("""
            {
              "name": "Aspirin",
              "dosage": "100 mg",
              "doseUnit": "tablet",
              "doseQuantity": 1,
              "doseTimes": ["08:00:00"],
              "startDate": "2026-09-01",
              "endDate": null,
              "instructions": null
            }
            """);

        Assert.Null(request.Stock);
    }

    [Fact]
    public void UpdateRequest_WhenStockPropertiesAreExplicitlyNull_IsCompleteAndClearsTracking()
    {
        var request = Deserialize("""
            {
              "name": "Aspirin",
              "dosage": "100 mg",
              "doseUnit": "tablet",
              "doseQuantity": 1,
              "doseTimes": ["08:00:00"],
              "startDate": "2026-09-01",
              "endDate": null,
              "instructions": null,
              "stock": { "stockQuantity": null, "lowStockThreshold": null }
            }
            """);

        Assert.NotNull(request.Stock);
        Assert.True(request.Stock.IsComplete);
        Assert.Null(request.Stock.StockQuantity);
        Assert.Null(request.Stock.LowStockThreshold);
    }

    [Fact]
    public void UpdateRequest_WhenStockPropertyIsOmitted_IsIncomplete()
    {
        var request = Deserialize("""
            {
              "name": "Aspirin",
              "dosage": "100 mg",
              "doseUnit": "tablet",
              "doseQuantity": 1,
              "doseTimes": ["08:00:00"],
              "startDate": "2026-09-01",
              "endDate": null,
              "instructions": null,
              "stock": { "stockQuantity": 12 }
            }
            """);

        Assert.NotNull(request.Stock);
        Assert.False(request.Stock.IsComplete);
    }

    [Fact]
    public async Task UpdateMedicationController_RejectsPartialStockObjectBeforeDispatch()
    {
        var sender = new CapturingSender();
        var controller = new MedicationsController(sender)
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

        var request = Deserialize("""
            {
              "name": "Aspirin",
              "dosage": "100 mg",
              "doseUnit": "tablet",
              "doseQuantity": 1,
              "doseTimes": ["08:00:00"],
              "startDate": "2026-09-01",
              "endDate": null,
              "instructions": null,
              "stock": { "stockQuantity": 12 }
            }
            """);

        var action = await controller.UpdateMedication(
            Guid.NewGuid(), Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task GetDoseHistoryController_RejectsMissingDateBeforeDispatch()
    {
        var sender = new CapturingSender();
        var controller = new MedicationsController(sender)
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

        var action = await controller.GetDoseHistory(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new MedicationDoseHistoryRequest(new DateOnly(2026, 9, 1), null),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action);
        Assert.Null(sender.LastRequest);
    }

    private static UpdateMedicationRequest Deserialize(string json) =>
        JsonSerializer.Deserialize<UpdateMedicationRequest>(json, WebJson)!;

    private sealed class CapturingSender : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            throw new InvalidOperationException("The controller should reject the request before dispatch.");
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
