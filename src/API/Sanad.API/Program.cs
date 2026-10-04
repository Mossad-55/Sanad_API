using Sanad.API;
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
    builder.Services.AddDataProtection()
        .UseEphemeralDataProtectionProvider();
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSanadApi(
    builder.Configuration);

var app = builder.Build();

app.UseSanadApi();

app.Run();

public partial class Program;
