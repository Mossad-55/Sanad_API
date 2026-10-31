using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Abstractions.Bookings;
using Ahlena.Modules.Families.Infrastructure.Bookings;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Infrastructure.Payments;
using Ahlena.Modules.Families.Infrastructure.Data;
using Ahlena.Modules.Finance.Application;

namespace Ahlena.Modules.Families.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFamiliesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString =
            configuration.GetConnectionString(
                "FamiliesDatabase")
            ?? configuration.GetConnectionString(
                "IdentityDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:FamiliesDatabase or " +
                "ConnectionStrings:IdentityDatabase is required.");
        }

        services.AddDbContext<FamiliesDbContext>(
            options =>
                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                        npgsqlOptions.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            FamiliesDbContext.Schema)));

        services.AddScoped<IFamiliesDbContext>(
            serviceProvider =>
                serviceProvider.GetRequiredService<
                    FamiliesDbContext>());

        services.AddScoped<IBookingCancellationFactRecorder,
            BookingCancellationFactRecorder>();

        services.AddScoped<ICaregiverBookingCheckoutGuard,
            CaregiverBookingReservationGuard>();

        services.AddHttpClient();

        services.Configure<PaymobOptions>(
            configuration.GetSection(PaymobOptions.SectionName));

        string? paymobSecretKey = configuration.GetSection(PaymobOptions.SectionName)["SecretKey"];

        // ------------------------------------------------------------------
        // Fixture-only refund-outcome seam.
        //
        // The real client (Development or production PaymobClient) is
        // registered under the key "paymob-inner"; the sole non-keyed
        // IPaymobClient is the seam decorator, so EVERY refund caller — care
        // -home refund/retry/expiry, Families booking refunds, and
        // SubscriptionRefundGateway — goes through one interception point
        // while non-refund operations delegate untouched. Default OFF: with
        // an absent/empty FixtureRefundSeam section the decorator is a pure
        // pass-through with zero behaviour change. ValidateOnStart refuses
        // Enabled=true in Production or a malformed outcome map.
        // ------------------------------------------------------------------
        services.AddOptions<FixtureRefundSeamOptions>()
            .Bind(configuration.GetSection(FixtureRefundSeamOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton
                <IValidateOptions<FixtureRefundSeamOptions>,
                 FixtureRefundSeamOptionsValidator>());

        if (string.IsNullOrWhiteSpace(paymobSecretKey))
        {
            services.AddKeyedSingleton<IPaymobClient, DevelopmentPaymobClient>("paymob-inner");
        }
        else
        {
            services.AddKeyedSingleton<IPaymobClient, PaymobClient>("paymob-inner");
        }

        services.AddSingleton<IPaymobClient>(serviceProvider =>
            new FixtureRefundSeamPaymobClient(
                serviceProvider.GetRequiredKeyedService<IPaymobClient>("paymob-inner"),
                serviceProvider.GetRequiredService<IOptionsMonitor<FixtureRefundSeamOptions>>(),
                serviceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<FixtureRefundSeamPaymobClient>()));

        // Finance's duplicate-charge claim execution resolves its provider
        // charge on the purchase owner's side: same client, same outcome
        // mapping as every other refund path.
        services.AddScoped<ISubscriptionRefundGateway, SubscriptionRefundGateway>();

        return services;
    }
}