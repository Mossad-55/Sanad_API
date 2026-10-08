using Microsoft.AspNetCore.Http;
using Sanad.API;

namespace Sanad.UnitTests.API;

public sealed class PrivateStorageRequestGuardTests
{
    [Theory]
    [InlineData("/files/private/license.pdf")]
    [InlineData("/files/PRIVATE/care-home-document.pdf")]
    public async Task Private_storage_paths_are_not_served_statistically(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        bool nextCalled = false;

        await new PrivateStorageRequestGuard().InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Public_storage_paths_continue_to_static_file_middleware()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/files/care-homes/public-image.jpg";
        bool nextCalled = false;

        await new PrivateStorageRequestGuard().InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
    }
}
