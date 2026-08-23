using Sanad.API;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSanadApi(
    builder.Configuration);

var app = builder.Build();

app.UseSanadApi();

app.Run();

public partial class Program;