using Sanad.API;
using Sanad.API.Seeding;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

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
