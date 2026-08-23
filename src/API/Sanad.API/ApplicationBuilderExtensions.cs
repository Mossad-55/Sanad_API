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

        app.MapOpenApi();

        return app;
    }
}