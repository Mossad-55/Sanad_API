namespace Sanad.API;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseSanadApi(
        this WebApplication app)
    {
        app.UseExceptionHandler();

        app.UseStatusCodePages();

        app.UseHttpsRedirection();

        app.UseAuthentication();

        app.UseAuthorization();

        app.MapControllers();

        app.MapOpenApi();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint(
                    "/openapi/v1.json",
                    "Sanad Care API v1");
            });
        }

        return app;
    }
}