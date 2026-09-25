using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Wellness;

namespace Sanad.UnitTests.API;

public sealed class WellnessTipsControllerTests
{
    [Fact]
    public void Controllers_RequireExpectedAuthorizationPolicies()
    {
        var admin = Assert.Single(typeof(AdminWellnessTipsController)
            .GetCustomAttributes<AuthorizeAttribute>(true));
        var elderly = Assert.Single(typeof(ElderlyWellnessTipsController)
            .GetCustomAttributes<AuthorizeAttribute>(true));
        Assert.Equal(AuthorizationPolicies.CmsContent, admin.Policy);
        Assert.Equal(AuthorizationPolicies.ElderlyAccess, elderly.Policy);
    }

    [Theory]
    [InlineData("Cms.WellnessTip.NotFound", 404)]
    [InlineData("Cms.WellnessTip.NotPublished", 404)]
    [InlineData("Cms.WellnessTip.InvalidOperation", 409)]
    public void WellnessTipErrors_MapToResourceAndConflictStatuses(string code, int expectedStatus)
    {
        var problem = ResultProblemDetailsMapper.Create(new Error(code, "test"), new DefaultHttpContext());
        Assert.Equal(expectedStatus, problem.Status);
    }

    [Fact]
    public async Task ElderlyFeedAndDetail_ForcePublishedOnlyQueries()
    {
        var sender = new CapturingSender(Result<WellnessTipPageResponse>.Success(new WellnessTipPageResponse([], 1, 20, 0)));
        var controller = new ElderlyWellnessTipsController(sender);

        Assert.IsType<OkObjectResult>(await controller.Feed(ct: CancellationToken.None));
        var feed = Assert.IsType<ListWellnessTipsQuery>(sender.LastRequest);
        Assert.True(feed.PublishedOnly);

        var id = Guid.NewGuid();
        sender.Response = Result<WellnessTipResponse>.Success(null!);
        Assert.IsType<OkObjectResult>(await controller.Detail(id, CancellationToken.None));
        var detail = Assert.IsType<GetWellnessTipQuery>(sender.LastRequest);
        Assert.Equal(id, detail.Id);
        Assert.True(detail.PublishedOnly);
    }

    [Fact]
    public async Task AdminCreate_MapsLocalizedOrderedSectionsAndReturnsCreated()
    {
        var response = new WellnessTipResponse(Guid.NewGuid(), "ar", "en", "sleep", "tip.png",
            Sanad.Modules.Cms.Domain.Wellness.WellnessTipPublicationStatus.Draft,
            DateTime.UtcNow, DateTime.UtcNow, null, []);
        var sender = new CapturingSender(Result<WellnessTipResponse>.Success(response));
        var controller = new AdminWellnessTipsController(sender, new TestFileStorage());
        var request = new CreateWellnessTipRequest("ar", "en", "sleep",
            JsonSerializer.Serialize(new[] { new WellnessTipSectionRequest(2, "two ar", "two en"), new WellnessTipSectionRequest(1, "one ar", "one en") }));
        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var file = new FormFile(new MemoryStream(pngSignature), 0, pngSignature.Length, "file", "tip.png");
        file.Headers = new HeaderDictionary { ["Content-Type"] = "image/png" };

        var result = await controller.Create(request, file, CancellationToken.None);

        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
        var command = Assert.IsType<CreateWellnessTipCommand>(sender.LastRequest);
        Assert.Equal([2, 1], command.Sections.Select(x => x.DisplayOrder));
        Assert.Equal("two en", command.Sections[0].EnglishText);
    }

    [Fact]
    public async Task AdminCreate_RejectsFileWithSpoofedImageContentType()
    {
        var sender = new CapturingSender(Result<WellnessTipResponse>.Success(null!));
        var storage = new TestFileStorage();
        var controller = new AdminWellnessTipsController(sender, storage);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var request = new CreateWellnessTipRequest("ar", "en", "sleep",
            JsonSerializer.Serialize(new[] { new WellnessTipSectionRequest(1, "ar", "en") }));
        var file = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "tip.png");
        file.Headers = new HeaderDictionary { ["Content-Type"] = "image/png" };

        var result = await controller.Create(request, file, CancellationToken.None);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(0, storage.SaveCount);
        Assert.Null(sender.LastRequest);
    }

    private sealed class TestFileStorage : IFileStorage
    {
        public int SaveCount { get; private set; }
        public Task<Result<StoredFile>> SaveAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) { SaveCount++; return Task.FromResult<Result<StoredFile>>(new StoredFile($"{folder}/safe.png")); }
        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class CapturingSender(object response) : ISender
    {
        public object? LastRequest { get; private set; }
        public object Response { get; set; } = response;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)Response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
