using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure;
using Sanad.Modules.Families.Infrastructure.Payments;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// Focused facts for the fixture-only refund-outcome seam
/// (<see cref="FixtureRefundSeamPaymobClient"/>): strict pass-through while
/// disabled, the exact provider-client result shapes for each scripted
/// outcome, the allowlist (never a global switch), fail-safe refusal on a
/// malformed configured outcome, non-refund delegation, and the startup
/// validation contract (Production refused; the outcome map validated even
/// while disabled). All offline — no database, no network, no provider.
/// </summary>
public sealed class FixtureRefundSeamTests
{
    private const string AllowlistedId = "spf-r2-seam-caregiver-failed-txn";
    private const string OtherAllowlistedId = "spf-r2-carehome-refund-failed-txn";
    private const string UnlistedId = "20261025001";

    // ------------------------------------------------------------------
    // Pass-through while disabled
    // ------------------------------------------------------------------

    [Fact]
    public async Task Disabled_Seam_Is_A_Strict_PassThrough_Even_For_Allowlisted_Ids()
    {
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: false, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "Rejected"
        });

        Result<string?> result = await seam.RefundPaymentAsync(AllowlistedId, 100m);

        Assert.True(result.IsSuccess);
        Assert.Equal("real-refund-ref", result.Value);
        Assert.Equal(1, inner.RefundCalls);
        Assert.Equal(AllowlistedId, inner.LastTransactionId);
        Assert.Equal(100m, inner.LastAmount);
    }

    [Fact]
    public async Task Absent_Configuration_Section_Binds_Defaults_And_Is_A_Pure_PassThrough()
    {
        RecordingClient inner = new();
        FixtureRefundSeamOptions options = new();
        Assert.False(options.Enabled);
        var seam = new FixtureRefundSeamPaymobClient(
            inner, new TestOptionsMonitor(options), NullLogger<FixtureRefundSeamPaymobClient>.Instance);

        Result<string?> result = await seam.RefundPaymentAsync(AllowlistedId, 10m);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, inner.RefundCalls);
    }

    // ------------------------------------------------------------------
    // The allowlist is never a global switch
    // ------------------------------------------------------------------

    [Fact]
    public async Task Enabled_Seam_Delegates_Non_Allowlisted_Refunds_To_The_Real_Client()
    {
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "Rejected"
        });

        Result<string?> result = await seam.RefundPaymentAsync(UnlistedId, 250m);

        Assert.True(result.IsSuccess);
        Assert.Equal("real-refund-ref", result.Value);
        Assert.Equal(1, inner.RefundCalls);
        Assert.Equal(UnlistedId, inner.LastTransactionId);
        Assert.Equal(250m, inner.LastAmount);
    }

    // ------------------------------------------------------------------
    // Scripted outcomes: the exact real-client shapes
    // ------------------------------------------------------------------

    [Fact]
    public async Task Allowlisted_Rejected_Maps_To_The_Real_Refusal_Shape_Without_Touching_The_Inner_Client()
    {
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "Rejected"
        });

        Result<string?> result = await seam.RefundPaymentAsync(AllowlistedId, 100m);

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.RefundRejected", result.Error.Code);
        Assert.Equal(0, inner.RefundCalls);
    }

    [Fact]
    public async Task Allowlisted_Unknown_Maps_To_The_Real_Unconfirmable_Shape_Without_Touching_The_Inner_Client()
    {
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "unknown"
        });

        Result<string?> result = await seam.RefundPaymentAsync(AllowlistedId, 100m);

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.RefundOutcomeUnknown", result.Error.Code);
        Assert.Equal("The refund outcome could not be confirmed.", result.Error.Message);
        Assert.Equal(0, inner.RefundCalls);
    }

    [Fact]
    public async Task Allowlisted_Completed_Takes_The_Confirmed_Refund_Success_Path_Without_Touching_The_Inner_Client()
    {
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "COMPLETED"
        });

        Result<string?> result = await seam.RefundPaymentAsync(AllowlistedId, 100m);

        Assert.True(result.IsSuccess);
        Assert.Equal($"fixture-refund-{AllowlistedId}", result.Value);
        Assert.Equal(0, inner.RefundCalls);
    }

    [Fact]
    public async Task Outcome_Parse_Is_Case_Insensitive_Across_All_Three_Scripted_Outcomes()
    {
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "Rejected",
            [OtherAllowlistedId] = "Completed"
        });

        Result<string?> rejected = await seam.RefundPaymentAsync(AllowlistedId, 100m);
        Result<string?> completed = await seam.RefundPaymentAsync(OtherAllowlistedId, 100m);

        Assert.Equal("Paymob.RefundRejected", rejected.Error.Code);
        Assert.True(completed.IsSuccess);
        Assert.Equal(0, inner.RefundCalls);
    }

    // ------------------------------------------------------------------
    // Verified readback: a synthetic fixture reference never reaches the real
    // provider; a real reference passes through read-only.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Synthetic_Fixture_Refund_Is_Never_Verified_Against_The_Real_Client()
    {
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "Completed"
        });

        // The seam's own scripted Completed refund produces a synthetic
        // reference; it proves nothing about a provider, so a readback for it
        // must be refused WITHOUT ever reaching the real client.
        Result<string?> refund = await seam.RefundPaymentAsync(AllowlistedId, 100m);
        Assert.Equal($"{FixtureRefundSeamPaymobClient.SyntheticRefundReferencePrefix}{AllowlistedId}", refund.Value);

        Result<PaymobRefundVerification> verification = await seam.VerifyRefundTransactionAsync(refund.Value!);

        Assert.True(verification.IsFailure);
        Assert.Equal("Paymob.VerificationUnavailable", verification.Error.Code);
        Assert.Equal(0, inner.VerifyCalls);
    }

    [Fact]
    public async Task Synthetic_Fixture_Refund_Is_Refused_Even_When_The_Seam_Is_Disabled()
    {
        // The synthetic guard is unconditional: a `fixture-refund-` reference
        // must never be sent to a provider regardless of the seam's enablement.
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: false, outcomes: new Dictionary<string, string>());

        Result<PaymobRefundVerification> verification =
            await seam.VerifyRefundTransactionAsync($"{FixtureRefundSeamPaymobClient.SyntheticRefundReferencePrefix}{AllowlistedId}");

        Assert.True(verification.IsFailure);
        Assert.Equal("Paymob.VerificationUnavailable", verification.Error.Code);
        Assert.Equal(0, inner.VerifyCalls);
    }

    [Fact]
    public async Task Verified_Readback_Delegates_A_Real_Transaction_To_The_Inner_Client()
    {
        // A real provider id (any value that is not the synthetic prefix) is
        // delegated read-only, so a genuine provider refund can still be
        // verified through the decorator.
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "Rejected"
        });

        Result<PaymobRefundVerification> verification = await seam.VerifyRefundTransactionAsync("561442898");

        Assert.Equal(1, inner.VerifyCalls);
        Assert.Equal("561442898", inner.LastVerifiedTransactionId);
        Assert.True(verification.IsFailure); // the recording stand-in refuses by contract
    }

    // ------------------------------------------------------------------
    // Fail-safe refusal on a malformed configured outcome
    // ------------------------------------------------------------------

    [Fact]
    public async Task Malformed_Configured_Outcome_Refuses_The_Refund_Without_Touching_The_Inner_Client()
    {
        // A configuration change after startup must never send the scripted
        // fixture id to the real provider: the seam refuses with the
        // unconfirmable shape (409/ambiguity) instead of guessing.
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "Exploded"
        });

        Result<string?> result = await seam.RefundPaymentAsync(AllowlistedId, 100m);

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.RefundOutcomeUnknown", result.Error.Code);
        Assert.Equal(0, inner.RefundCalls);
    }

    // ------------------------------------------------------------------
    // Live configuration: reloads are honoured on the next call. The
    // monitor carries the REAL validator (as the composition root does), so
    // a VALID reload updates the monitored value and an INVALID reload
    // makes it unreadable — the decorator must fail closed in that case.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Reload_Changing_An_Allowlisted_Outcome_Is_Observed_On_The_Next_Call()
    {
        // Proves the seam reads options LIVE (IOptionsMonitor.CurrentValue,
        // not an IOptions snapshot) through the VALIDATED pipeline: Rejected
        // at start, Completed after a config reload — and the inner client
        // is still never touched.
        using ConfigBackedMonitor configMonitor = NewConfigBackedMonitor(new Dictionary<string, string?>
        {
            ["FixtureRefundSeam:Enabled"] = "true",
            [$"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}"] = "Rejected"
        });
        RecordingClient inner = new();
        using var seam = new FixtureRefundSeamPaymobClient(
            inner, configMonitor.Monitor, NullLogger<FixtureRefundSeamPaymobClient>.Instance);

        Result<string?> beforeReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
        Assert.Equal("Paymob.RefundRejected", beforeReload.Error.Code);

        configMonitor.Provider.Set(
            $"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}", "Completed");
        configMonitor.Root.Reload();

        Result<string?> afterReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
        Assert.True(afterReload.IsSuccess);
        Assert.Equal($"fixture-refund-{AllowlistedId}", afterReload.Value);
        Assert.Equal(0, inner.RefundCalls);
    }

    [Fact]
    public async Task Reload_To_An_Unparseable_Value_Fails_Closed_And_Never_Serves_A_Stale_Outcome()
    {
        // The registered validator is enforced on every monitored-value
        // creation INCLUDING right after a reload, so the reloaded garbage
        // value makes the monitored options unreadable: the change callback
        // re-validates eagerly and may surface OptionsValidationException at
        // the reload site (tolerated explicitly here, as the decorator
        // tolerates it), and EVERY subsequent refund call fails closed with
        // the unconfirmable shape — the inner client is never touched. This
        // also proves no parsed-outcome cache can serve a stale value.
        using ConfigBackedMonitor configMonitor = NewConfigBackedMonitor(new Dictionary<string, string?>
        {
            ["FixtureRefundSeam:Enabled"] = "true",
            [$"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}"] = "Rejected"
        });
        RecordingClient inner = new();
        using var seam = new FixtureRefundSeamPaymobClient(
            inner, configMonitor.Monitor, NullLogger<FixtureRefundSeamPaymobClient>.Instance);

        Result<string?> beforeReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
        Assert.Equal("Paymob.RefundRejected", beforeReload.Error.Code);

        configMonitor.Provider.Set(
            $"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}", "garbage-after-reload");
        ReloadToleratingOptionsValidationException(configMonitor.Root);

        // The allowlisted id is refused fail-closed...
        Result<string?> allowlistedAfterReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
        Assert.True(allowlistedAfterReload.IsFailure);
        Assert.Equal("Paymob.RefundOutcomeUnknown", allowlistedAfterReload.Error.Code);

        // ...and so is the UNLISTED id: with invalid options the allowlist
        // itself is unknowable, so pass-through is suspended while the
        // configuration is invalid (fail closed, never guess).
        Result<string?> unlistedAfterReload = await seam.RefundPaymentAsync(UnlistedId, 100m);
        Assert.True(unlistedAfterReload.IsFailure);
        Assert.Equal("Paymob.RefundOutcomeUnknown", unlistedAfterReload.Error.Code);

        Assert.Equal(0, inner.RefundCalls);
    }

    [Fact]
    public async Task Reload_Enabling_The_Seam_Under_Production_Is_Refused_And_Fails_Closed()
    {
        // The Production guard is absolute: it holds at startup AND on a
        // later reload. A Production host that legitimately ran with the
        // seam disabled CANNOT be enabled by a config reload — the
        // re-validated options refuse (invalid), and every refund call fails
        // closed with the unconfirmable shape instead of delegating to the
        // real provider or returning a scripted outcome.
        using ConfigBackedMonitor configMonitor = NewConfigBackedMonitor(
            new Dictionary<string, string?>
            {
                ["FixtureRefundSeam:Enabled"] = "false",
                [$"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}"] = "Rejected"
            },
            environmentName: Environments.Production);
        RecordingClient inner = new();
        using var seam = new FixtureRefundSeamPaymobClient(
            inner, configMonitor.Monitor, NullLogger<FixtureRefundSeamPaymobClient>.Instance);

        // While disabled, the seam is a strict pass-through even for the
        // allowlisted id.
        Result<string?> beforeReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
        Assert.True(beforeReload.IsSuccess);
        Assert.Equal(1, inner.RefundCalls);

        configMonitor.Provider.Set("FixtureRefundSeam:Enabled", "true");
        ReloadToleratingOptionsValidationException(configMonitor.Root);

        // The enablement attempt never yields a scripted seam outcome and
        // never reaches the provider: the options are invalid (Production
        // refused on every re-validation), so the call fails closed.
        Result<string?> afterReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
        Assert.True(afterReload.IsFailure);
        Assert.Equal("Paymob.RefundOutcomeUnknown", afterReload.Error.Code);
        Assert.Equal(1, inner.RefundCalls);
    }

    // ------------------------------------------------------------------
    // Reloads through the REAL composition root (the deployed decorator and
    // its host's own options pipeline, not a test-built monitor).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Real_Host_Valid_Reload_Is_Observed_By_The_Decorator()
    {
        // Offline-client pin (see the Production fact): the blank secret key
        // makes the composition resolve the offline DevelopmentPaymobClient
        // even on machines with ambient Paymob__SecretKey environment
        // variables, so no leg of this fact can arm the REAL provider client.
        ReloadableHost started = await StartReloadableHostAsync(
            environmentName: Environments.Development,
            configuration: new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = HostConfigConnectionString,
                ["Paymob:SecretKey"] = "",
                ["FixtureRefundSeam:Enabled"] = "true",
                [$"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}"] = "Rejected"
            });

        try
        {
            IPaymobClient seam = started.Host.Services.GetRequiredService<IPaymobClient>();

            Result<string?> beforeReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
            Assert.Equal("Paymob.RefundRejected", beforeReload.Error.Code);

            started.MemoryProvider.Set(
                $"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}", "Completed");
            started.Root.Reload(); // a VALID reload must not throw

            // The `fixture-refund-` reference is produced ONLY by the seam
            // decorator, so its presence proves the host's own live options
            // pipeline delivered the reload to the deployed decorator.
            Result<string?> afterReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
            Assert.True(afterReload.IsSuccess);
            Assert.Equal($"fixture-refund-{AllowlistedId}", afterReload.Value);
        }
        finally
        {
            await StopQuietlyAsync(started.Host);
        }
    }

    [Fact]
    public async Task Real_Host_Invalid_Reload_Fails_Closed_And_Never_Reaches_The_Inner_Client()
    {
        // Offline-client pin (see the Production fact): no leg of this fact
        // may ever arm the REAL provider client.
        ReloadableHost started = await StartReloadableHostAsync(
            environmentName: Environments.Development,
            configuration: new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = HostConfigConnectionString,
                ["Paymob:SecretKey"] = "",
                ["FixtureRefundSeam:Enabled"] = "true",
                [$"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}"] = "Rejected"
            });

        try
        {
            IPaymobClient seam = started.Host.Services.GetRequiredService<IPaymobClient>();

            Result<string?> beforeReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
            Assert.Equal("Paymob.RefundRejected", beforeReload.Error.Code);

            started.MemoryProvider.Set(
                $"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}", "garbage-after-reload");
            ReloadToleratingOptionsValidationException(started.Root);

            // The allowlisted id fails closed...
            Result<string?> allowlisted = await seam.RefundPaymentAsync(AllowlistedId, 100m);
            Assert.True(allowlisted.IsFailure);
            Assert.Equal("Paymob.RefundOutcomeUnknown", allowlisted.Error.Code);

            // ...and so does the unlisted id: with invalid options the
            // allowlist is unknowable, so pass-through is suspended rather
            // than risk sending a scripted fixture id to the real provider.
            Result<string?> unlisted = await seam.RefundPaymentAsync(UnlistedId, 100m);
            Assert.True(unlisted.IsFailure);
            Assert.Equal("Paymob.RefundOutcomeUnknown", unlisted.Error.Code);
        }
        finally
        {
            await StopQuietlyAsync(started.Host);
        }
    }

    [Fact]
    public async Task Real_Host_Production_Cannot_Enable_The_Seam_Via_Reload()
    {
        // Startup refusal is retained (Production_Host_With_Enabled_Seam_
        // Refuses_To_Start); this fact proves the reload direction: a
        // Production host running the seam DISABLED cannot be enabled after
        // startup — the re-validated options refuse, and every refund call
        // fails closed with the unconfirmable shape (never a scripted seam
        // outcome, never a provider call).
        // The pass-through leg delegates to the keyed INNER client, so the
        // fixture MUST pin the offline client: blanking the secret key makes
        // the composition resolve DevelopmentPaymobClient. Without this, a
        // machine-level Paymob__SecretKey environment variable (loaded by
        // Host.CreateApplicationBuilder) would silently arm the REAL provider
        // client and turn this fact into an outbound provider call.
        ReloadableHost started = await StartReloadableHostAsync(
            environmentName: Environments.Production,
            configuration: new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = HostConfigConnectionString,
                ["Paymob:SecretKey"] = "",
                ["FixtureRefundSeam:Enabled"] = "false",
                [$"FixtureRefundSeam:RefundOutcomes:{AllowlistedId}"] = "Rejected"
            });

        try
        {
            IPaymobClient seam = started.Host.Services.GetRequiredService<IPaymobClient>();

            // Disabled: strict pass-through for the allowlisted id.
            Result<string?> beforeReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
            Assert.True(beforeReload.IsSuccess);

            started.MemoryProvider.Set("FixtureRefundSeam:Enabled", "true");
            ReloadToleratingOptionsValidationException(started.Root);

            Result<string?> afterReload = await seam.RefundPaymentAsync(AllowlistedId, 100m);
            Assert.True(afterReload.IsFailure);
            Assert.Equal("Paymob.RefundOutcomeUnknown", afterReload.Error.Code);
        }
        finally
        {
            await StopQuietlyAsync(started.Host);
        }
    }

    // ------------------------------------------------------------------
    // An enabled seam with an empty allowlist is refused
    // ------------------------------------------------------------------

    [Fact]
    public void Validator_Refuses_Enabled_Seam_With_An_Empty_Allowlist()
    {
        // Enabled=true with zero RefundOutcomes entries is a no-op that
        // LOOKS on — exactly the state that would make --seam-deployed a
        // lie. It must fail application start.
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var validator = new FixtureRefundSeamOptionsValidator(provider);

        ValidateOptionsResult result = validator.Validate(
            FixtureRefundSeamOptions.SectionName,
            new FixtureRefundSeamOptions { Enabled = true, RefundOutcomes = [] });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            message => message.Contains("an empty RefundOutcomes map is refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_With_Enabled_Seam_And_An_Empty_Allowlist_Refuses_To_Start()
    {
        // The empty-map refusal through the REAL composition root, mirroring
        // the Production host-start fact.
        (IHost Host, Exception? Failure) started = await TryStartHostAsync(
            environmentName: Environments.Development,
            configuration: new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = HostConfigConnectionString,
                ["FixtureRefundSeam:Enabled"] = "true"
            });

        try
        {
            Assert.NotNull(started.Failure);
            string message = Flatten(started.Failure!);
            Assert.Contains("an empty RefundOutcomes map is refused", message);
        }
        finally
        {
            await StopQuietlyAsync(started.Host);
        }
    }

    // ------------------------------------------------------------------
    // Non-refund operations delegate untouched
    // ------------------------------------------------------------------

    [Fact]
    public async Task Non_Refund_Operations_Always_Delegate_To_The_Inner_Client()
    {
        RecordingClient inner = new();
        FixtureRefundSeamPaymobClient seam = NewSeam(inner, enabled: true, outcomes: new Dictionary<string, string>
        {
            [AllowlistedId] = "Rejected"
        });

        var billing = new PaymobBillingData("a", "b", "c@d.local", "+201000000000");
        var intent = await seam.CreatePaymentIntentAsync(
            new PaymobPaymentIntentInput(BookingId.New(), PaymentMethod.Card, 100m, "EGP", billing));
        var subscriptionIntent = await seam.CreateSubscriptionPaymentIntentAsync(
            new PaymobSubscriptionPaymentIntentInput(
                "ref", SubscriptionPaymentMethod.Card, 100m, "EGP", billing));
        Result updated = await seam.UpdateSubscriptionAmountAsync("provider-sub", 150m);

        Assert.True(intent.IsSuccess);
        Assert.Equal(1, inner.IntentCalls);
        Assert.True(subscriptionIntent.IsSuccess);
        Assert.Equal(1, inner.SubscriptionIntentCalls);
        Assert.True(updated.IsSuccess);
        Assert.Equal(1, inner.UpdateCalls);
        Assert.Equal(0, inner.RefundCalls);
    }

    // ------------------------------------------------------------------
    // Startup validation contract
    // ------------------------------------------------------------------

    [Fact]
    public void Validator_Refuses_Enabled_Seam_In_Production()
    {
        using ServiceProvider provider = EnvironmentProvider(Environments.Production);
        var validator = new FixtureRefundSeamOptionsValidator(provider);

        ValidateOptionsResult result = validator.Validate(
            FixtureRefundSeamOptions.SectionName,
            new FixtureRefundSeamOptions { Enabled = true });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            message => message.Contains("forbidden in the Production environment", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_Accepts_Enabled_Seam_In_Development()
    {
        using ServiceProvider provider = EnvironmentProvider(Environments.Development);
        var validator = new FixtureRefundSeamOptionsValidator(provider);

        ValidateOptionsResult result = validator.Validate(
            FixtureRefundSeamOptions.SectionName,
            new FixtureRefundSeamOptions
            {
                Enabled = true,
                RefundOutcomes = new Dictionary<string, string> { [AllowlistedId] = "Rejected" }
            });

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validator_Refuses_Enabled_Seam_Without_A_Resolvable_Environment()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var validator = new FixtureRefundSeamOptionsValidator(provider);

        ValidateOptionsResult result = validator.Validate(
            FixtureRefundSeamOptions.SectionName,
            new FixtureRefundSeamOptions { Enabled = true });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            message => message.Contains("environment could not be determined", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_Validates_The_Outcome_Map_Even_While_Disabled()
    {
        using ServiceProvider provider = EnvironmentProvider(Environments.Development);
        var validator = new FixtureRefundSeamOptionsValidator(provider);

        ValidateOptionsResult result = validator.Validate(
            FixtureRefundSeamOptions.SectionName,
            new FixtureRefundSeamOptions
            {
                Enabled = false,
                RefundOutcomes = new Dictionary<string, string> { [AllowlistedId] = "arbitrary" }
            });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            message => message.Contains("Rejected, Unknown or Completed", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_Refuses_An_Empty_Transaction_Id_Key()
    {
        using ServiceProvider provider = EnvironmentProvider(Environments.Development);
        var validator = new FixtureRefundSeamOptionsValidator(provider);

        ValidateOptionsResult result = validator.Validate(
            FixtureRefundSeamOptions.SectionName,
            new FixtureRefundSeamOptions
            {
                Enabled = true,
                RefundOutcomes = new Dictionary<string, string> { [""] = "Rejected" }
            });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            message => message.Contains("empty transaction id key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Production_Host_With_Enabled_Seam_Refuses_To_Start()
    {
        // The proof of the startup contract through the REAL composition root:
        // the production registration binds FixtureRefundSeam with
        // ValidateOnStart, so Enabled=true under the Production environment
        // must fail host start with the validator's message.
        (IHost Host, Exception? Failure) started = await TryStartHostAsync(
            environmentName: Environments.Production,
            configuration: new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = HostConfigConnectionString,
                ["FixtureRefundSeam:Enabled"] = "true"
            });

        try
        {
            Assert.NotNull(started.Failure);
            string message = Flatten(started.Failure!);
            Assert.Contains("forbidden in the Production environment", message);
        }
        finally
        {
            await StopQuietlyAsync(started.Host);
        }
    }

    [Fact]
    public async Task Production_Host_With_Absent_Seam_Section_Starts_Clean()
    {
        // Control: the same host with no FixtureRefundSeam section starts fine
        // and the sole non-keyed IPaymobClient is the (OFF) seam decorator.
        (IHost Host, Exception? Failure) started = await TryStartHostAsync(
            environmentName: Environments.Production,
            configuration: new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = HostConfigConnectionString
            });

        try
        {
            Assert.Null(started.Failure);
            IPaymobClient client = started.Host.Services.GetRequiredService<IPaymobClient>();
            Assert.IsType<FixtureRefundSeamPaymobClient>(client);
        }
        finally
        {
            await StopQuietlyAsync(started.Host);
        }
    }

    [Fact]
    public async Task Registration_Wraps_The_Inner_Client_As_The_Sole_NonKeyed_Refund_Path()
    {
        (IHost Host, Exception? Failure) started = await TryStartHostAsync(
            environmentName: Environments.Development,
            configuration: new Dictionary<string, string?>
            {
                ["ConnectionStrings:IdentityDatabase"] = HostConfigConnectionString,
                ["Paymob:SecretKey"] = "unit-test-unused-value"
            });

        try
        {
            Assert.Null(started.Failure);
            IPaymobClient client = started.Host.Services.GetRequiredService<IPaymobClient>();
            Assert.IsType<FixtureRefundSeamPaymobClient>(client);

            // The keyed inner client is the REAL provider client (secret key
            // configured); the decorator wraps it and is the only non-keyed
            // IPaymobClient every refund caller resolves.
            IPaymobClient inner = started.Host.Services.GetRequiredKeyedService<IPaymobClient>(InnerClientKey);
            Assert.IsType<PaymobClient>(inner);
        }
        finally
        {
            await StopQuietlyAsync(started.Host);
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private const string HostConfigConnectionString =
        "Host=localhost;Port=5432;Database=unittest;Username=unittest;Password=unittest";

    private const string InnerClientKey = "paymob-inner";

    private static FixtureRefundSeamPaymobClient NewSeam(
        RecordingClient inner,
        bool enabled,
        Dictionary<string, string> outcomes)
    {
        return new FixtureRefundSeamPaymobClient(
            inner,
            new TestOptionsMonitor(new FixtureRefundSeamOptions
            {
                Enabled = enabled,
                RefundOutcomes = outcomes
            }),
            NullLogger<FixtureRefundSeamPaymobClient>.Instance);
    }

    /// <summary>
    /// A mutable IOptionsMonitor stand-in: CurrentValue is settable per test
    /// (the seam reads it live on every refund call, which is exactly the
    /// reload contract). The reload-specific facts use a REAL configuration-
    /// backed monitor (see NewConfigBackedMonitor) instead.
    /// </summary>
    private sealed class TestOptionsMonitor : IOptionsMonitor<FixtureRefundSeamOptions>
    {
        public TestOptionsMonitor(FixtureRefundSeamOptions current)
        {
            CurrentValue = current;
        }

        public FixtureRefundSeamOptions CurrentValue { get; set; }

        public FixtureRefundSeamOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<FixtureRefundSeamOptions, string?> listener) => null;
    }

    /// <summary>
    /// A REAL configuration-backed IOptionsMonitor built over a mutable
    /// MemoryConfigurationProvider with the SAME options pipeline the real
    /// composition root registers: <c>Bind</c> plus the
    /// <see cref="FixtureRefundSeamOptionsValidator"/> as an
    /// <c>IValidateOptions</c> (which the options pipeline enforces on EVERY
    /// monitored-value creation, reload included), over a settable host
    /// environment. Mutate the provider, call <c>IConfigurationRoot.Reload()</c>,
    /// and the monitor re-creates and re-validates the value: a valid reload
    /// updates CurrentValue; an invalid reload makes CurrentValue throw
    /// <see cref="OptionsValidationException"/> from then on — the exact
    /// production behaviour.
    /// </summary>
    private static ConfigBackedMonitor NewConfigBackedMonitor(
        Dictionary<string, string?> initialConfiguration,
        string? environmentName = null)
    {
        environmentName ??= Environments.Development;

        var source = new MemoryConfigurationSource { InitialData = initialConfiguration };
        IConfigurationRoot root = new ConfigurationBuilder().Add(source).Build();
        var provider = (MemoryConfigurationProvider)root.Providers.Single(provider => provider is MemoryConfigurationProvider);

        ServiceCollection services = new();
        services.AddOptions<FixtureRefundSeamOptions>()
            .Bind(root.GetSection(FixtureRefundSeamOptions.SectionName));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton
                <IValidateOptions<FixtureRefundSeamOptions>,
                 FixtureRefundSeamOptionsValidator>());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        ServiceProvider serviceProvider = services.BuildServiceProvider();
        IOptionsMonitor<FixtureRefundSeamOptions> monitor =
            serviceProvider.GetRequiredService<IOptionsMonitor<FixtureRefundSeamOptions>>();
        return new ConfigBackedMonitor(provider, root, monitor, serviceProvider);
    }

    private sealed record ConfigBackedMonitor(
        MemoryConfigurationProvider Provider,
        IConfigurationRoot Root,
        IOptionsMonitor<FixtureRefundSeamOptions> Monitor,
        ServiceProvider Services) : IDisposable
    {
        public void Dispose() => Services.Dispose();
    }

    private static ServiceProvider EnvironmentProvider(string environmentName)
    {
        return new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName))
            .BuildServiceProvider();
    }

    /// <summary>
    /// Reloads a configuration root while EXPLICITLY tolerating the
    /// options pipeline's eager re-validation: the monitor's change callback
    /// re-creates and re-validates the value synchronously during Reload(),
    /// and a refused reload surfaces as an OptionsValidationException —
    /// possibly wrapped in an AggregateException by the change-token
    /// callback dispatcher. This is the same exception the deployed
    /// decorator tolerates from its own change listener.
    /// </summary>
    private static void ReloadToleratingOptionsValidationException(IConfigurationRoot root)
    {
        try
        {
            root.Reload();
        }
        catch (Exception exception) when (HasOptionsValidationException(exception))
        {
            // Expected for an invalid reload.
        }

        static bool HasOptionsValidationException(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is OptionsValidationException)
                {
                    return true;
                }

                if (current is AggregateException aggregate
                    && aggregate.InnerExceptions.Any(HasOptionsValidationException))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static async Task<(IHost Host, Exception? Failure)> TryStartHostAsync(
        string environmentName,
        Dictionary<string, string?> configuration)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = environmentName;
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddFamiliesInfrastructure(builder.Configuration);

        IHost host = builder.Build();
        try
        {
            await host.StartAsync();
            return (host, null);
        }
        catch (Exception exception)
        {
            return (host, exception);
        }
    }

    /// <summary>
    /// A started host whose configuration the test can RELOAD: the retained
    /// root IS the root the real options pipeline bound against, so
    /// <c>Root.Reload()</c> exercises the deployed decorator through the
    /// host's own live options pipeline (bind + registered validator +
    /// ValidateOnStart), not a test-built stand-in.
    /// </summary>
    private sealed record ReloadableHost(IHost Host, IConfigurationRoot Root, MemoryConfigurationProvider MemoryProvider);

    private static async Task<ReloadableHost> StartReloadableHostAsync(
        string environmentName,
        Dictionary<string, string?> configuration)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = environmentName;
        builder.Configuration.AddInMemoryCollection(configuration);
        IConfigurationRoot root = builder.Configuration;
        var memoryProvider = (MemoryConfigurationProvider)
            ((IConfigurationRoot)builder.Configuration).Providers.Last(provider => provider is MemoryConfigurationProvider);
        builder.Services.AddFamiliesInfrastructure(builder.Configuration);

        IHost host = builder.Build();
        await host.StartAsync(); // validator refusals (if any) throw here, failing the fact
        return new ReloadableHost(host, root, memoryProvider);
    }

    private static async Task StopQuietlyAsync(IHost host)
    {
        try
        {
            await host.StopAsync();
        }
        catch
        {
            // A refused host never really started; stopping is best-effort.
        }

        host.Dispose();
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Sanad.UnitTests";
        public string ContentRootPath { get; set; } = System.IO.Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new TestFileProvider();
    }

    private sealed class TestFileProvider : Microsoft.Extensions.FileProviders.IFileProvider
    {
        public Microsoft.Extensions.FileProviders.IDirectoryContents GetDirectoryContents(string subPath) =>
            new Microsoft.Extensions.FileProviders.NotFoundDirectoryContents();

        public Microsoft.Extensions.FileProviders.IFileInfo GetFileInfo(string subPath) =>
            new Microsoft.Extensions.FileProviders.NotFoundFileInfo(subPath);

        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter) => new TestChangeToken();
    }

    private sealed class TestChangeToken : Microsoft.Extensions.Primitives.IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// A recording stand-in for the real client: counts every call so the
    /// facts can prove the inner client is (or is not) touched.
    /// </summary>
    private sealed class RecordingClient : IPaymobClient
    {
        public int RefundCalls { get; private set; }
        public string? LastTransactionId { get; private set; }
        public decimal LastAmount { get; private set; }
        public int IntentCalls { get; private set; }
        public int SubscriptionIntentCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int VerifyCalls { get; private set; }
        public string? LastVerifiedTransactionId { get; private set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default)
        {
            IntentCalls++;
            return Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent("order", "intention", "secret", "pk")));
        }

        public Task<Result<PaymobPaymentIntent>> CreateSubscriptionPaymentIntentAsync(
            PaymobSubscriptionPaymentIntentInput input,
            CancellationToken cancellationToken = default)
        {
            SubscriptionIntentCalls++;
            return Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent("order", "intention", "secret", "pk", RecurringRenewalSupported: true)));
        }

        public Task<Result> UpdateSubscriptionAmountAsync(
            string providerSubscriptionId,
            decimal targetRecurringGross,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            LastTransactionId = paymobTransactionId;
            LastAmount = amount;
            return Task.FromResult(Result<string?>.Success("real-refund-ref"));
        }

        public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
            string refundTransactionId,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            LastVerifiedTransactionId = refundTransactionId;
            return Task.FromResult(Result<PaymobRefundVerification>.Failure(new Error(
                "Paymob.VerificationUnavailable",
                "Recording stand-in does not verify refunds.")));
        }
    }
}
