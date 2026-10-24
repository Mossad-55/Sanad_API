using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Sanad.API.Controllers;
using Sanad.API.Deployment;
using Sanad.API.ProblemDetail;
using Sanad.API.Seeding;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.UnitTests.API;

/// <summary>
/// Acceptance rules for the Rentals removal (testing contract, "Removal and
/// schema evolution"): Rentals is absent from the runtime migration
/// enumeration, from active routes and DI, and from consumer contracts, while
/// the deployed schema and history are retained and the historical records are
/// untouched. The remaining context set is the nine module contexts, and the
/// preserved complaint-award cap mapping is not collateral damage of the
/// removal.
/// </summary>
public sealed class RentalsRemovalRegressionTests
{
    private static readonly string[] RemainingContexts =
    [
        "Caregivers",
        "CareHomes",
        "Chat",
        "Cms",
        "Community",
        "Families",
        "Finance",
        "Identity",
        "Notifications"
    ];

    private const string LocalConnectionString =
        "Host=localhost;Port=5432;Database=SanadDb;Username=fixture;Password=fixture";

    private static readonly string[] RemovedControllerTypes =
    [
        "Sanad.API.Controllers.AdminRentalsController",
        "Sanad.API.Controllers.AdminRentalOperationsController",
        "Sanad.API.Controllers.AdminRentalRefundCasesController",
        "Sanad.API.Controllers.FamilyRentalsController"
    ];

    [Fact]
    public async Task Deployment_probe_reports_exactly_the_remaining_nine_contexts()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var probe = new EfDeploymentMigrationProbe(provider);

        (bool reachable, IReadOnlyDictionary<string, string?> applied) =
            await probe.GetAppliedAsync(default);

        // An empty provider cannot resolve a DbContext, so every probed
        // context is reported unreachable; the probe still enumerates the
        // runtime context set without touching any database.
        Assert.False(reachable);
        Assert.Equal<IEnumerable<string>>(
            RemainingContexts.OrderBy(name => name, StringComparer.Ordinal),
            applied.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.DoesNotContain(
            applied.Keys,
            name => name.Contains("Rental", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Removed_rental_controllers_and_context_are_no_longer_shipped()
    {
        Assembly api = typeof(PaymobWebhookController).Assembly;

        foreach (string removed in RemovedControllerTypes)
        {
            Assert.Null(api.GetType(removed, throwOnError: false));
        }

        Assert.Null(api.GetType(
            "Sanad.Modules.Rentals.Infrastructure.RentalsDbContext",
            throwOnError: false));
    }

    [Fact]
    public void Shipped_product_assemblies_expose_no_rental_types()
    {
        Assembly[] shipped =
        [
            typeof(PaymobWebhookController).Assembly,                          // Sanad.API
            typeof(Sanad.Modules.Finance.Infrastructure.FinanceDbContext).Assembly,
            typeof(Sanad.Modules.Finance.Application.SubscriptionRefundErrors).Assembly
        ];

        string[] offenders = shipped
            .SelectMany(LoadedTypes)
            .Select(type => type.FullName ?? string.Empty)
            .Where(name => name.Contains("Rental", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("Rentals.Catalog.NotFound")]
    [InlineData("Rentals.Booking.InvalidOperation")]
    [InlineData("Rentals.Booking.Conflict")]
    [InlineData("Rentals.Refund.DestinationUnavailable")]
    [InlineData("Rentals.Operations.BookingNotFound")]
    [InlineData("Rentals.Operations.InvalidOperation")]
    [InlineData("Finance.RentalRefund.ReconciliationRequired")]
    [InlineData("Finance.RentalRefund.Conflict")]
    public void Removed_rental_error_codes_fall_back_to_the_unmapped_status(string code)
    {
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            StatusCodeFor(code));
    }

    [Fact]
    public void Preserved_complaint_award_cap_mapping_survives_the_removal()
    {
        Assert.Equal(
            StatusCodes.Status409Conflict,
            StatusCodeFor("Finance.Complaint.AwardExceedsCap"));
    }

    [Fact]
    public void Startup_guard_ignores_a_stale_rentals_connection_and_still_validates_the_rest()
    {
        const string staleRentals =
            "Host=rentals.example.invalid;Port=5432;Database=rentals_db";
        var configuration = new ConfigurationManager
        {
            ["App:TestUserSeed:Enabled"] = "true",
            ["ConnectionStrings:IdentityDatabase"] = LocalConnectionString,
            ["ConnectionStrings:RentalsDatabase"] = staleRentals
        };

        TestUserSeedTargetGuard.PinToApprovedDatabase(
            configuration,
            new DevelopmentHostEnvironment());

        // The removed context is no longer enumerated: its stale connection is
        // neither required to be local nor rewritten.
        Assert.Equal(staleRentals, configuration.GetConnectionString("RentalsDatabase"));
        Assert.Contains(
            "SanadBrunoTestDb",
            configuration.GetConnectionString("IdentityDatabase"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_guard_still_refuses_a_non_local_connection_for_a_remaining_context()
    {
        var configuration = new ConfigurationManager
        {
            ["App:TestUserSeed:Enabled"] = "true",
            ["ConnectionStrings:IdentityDatabase"] = LocalConnectionString,
            ["ConnectionStrings:FinanceDatabase"] =
                "Host=finance.example.invalid;Port=5432;Database=finance_db"
        };

        Assert.Throws<InvalidOperationException>(
            () => TestUserSeedTargetGuard.PinToApprovedDatabase(
                configuration,
                new DevelopmentHostEnvironment()));
    }

    private static int StatusCodeFor(string code) =>
        ResultProblemDetailsMapper
            .Create(new Error(code, "removal regression"), new DefaultHttpContext())
            .Status ?? -1;

    private static IEnumerable<Type> LoadedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private sealed class DevelopmentHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Sanad.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
