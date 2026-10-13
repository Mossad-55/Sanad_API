using Sanad.API;
using Sanad.API.Options;
using Sanad.API.Seeding;
using Microsoft.AspNetCore.DataProtection;
using Sanad.Modules.Families.Infrastructure;
using Sanad.Modules.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

if (Hc034FamilyFixtureProvisioner.IsRequested(args))
{
    Hc034FamilyFixtureProvisioner.ValidateTarget(builder.Configuration, builder.Environment);
    builder.Services.AddIdentityInfrastructure(builder.Configuration);
    builder.Services.AddFamiliesInfrastructure(builder.Configuration);
    using WebApplication provisionerHost = builder.Build();
    using IServiceScope provisionerScope = provisionerHost.Services.CreateScope();
    await Hc034FamilyFixtureProvisioner.ProvisionAsync(provisionerScope.ServiceProvider, builder.Configuration);
    return;
}

TestUserSeedTargetGuard.PinToApprovedDatabase(
    builder.Configuration,
    builder.Environment);

if (builder.Configuration.GetValue<bool>($"{TestUserSeedOptions.SectionName}:Enabled"))
{
    // Fixture-seeding runs only (guarded to Development + the disposable
    // local target by TestUserSeedTargetGuard). Ephemeral keys are
    // acceptable here and must never be used on a publicly reachable host.
    builder.Services.AddDataProtection()
        .UseEphemeralDataProtectionProvider();
}
else
{
    // Persistent key ring for every other host, including Staging and
    // Production. Chat sync cursors are DataProtection-protected, so old
    // cursor tokens only survive restarts/redeploys with stable keys and a
    // stable application discriminator. Validation refuses ephemeral or
    // release-folder keys outside Development.
    SanadDataProtectionOptions dataProtectionOptions =
        builder.Configuration
            .GetSection(SanadDataProtectionOptions.SectionName)
            .Get<SanadDataProtectionOptions>()
        ?? new SanadDataProtectionOptions();

    dataProtectionOptions.Validate(
        builder.Environment.IsDevelopment(),
        builder.Environment.ContentRootPath);

    IDataProtectionBuilder dataProtectionBuilder =
        builder.Services.AddDataProtection();

    dataProtectionBuilder.SetApplicationName(
        dataProtectionOptions.ApplicationName);

    if (!string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
    {
        Directory.CreateDirectory(dataProtectionOptions.KeyRingPath);
        dataProtectionBuilder.PersistKeysToFileSystem(
            new DirectoryInfo(dataProtectionOptions.KeyRingPath));
    }
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSanadApi(
    builder.Configuration);

var app = builder.Build();

app.UseSanadApi();

app.Run();

public partial class Program;
