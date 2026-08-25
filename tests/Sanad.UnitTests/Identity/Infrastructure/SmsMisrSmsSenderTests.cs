using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Infrastructure.Messaging;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class SmsMisrSmsSenderTests
{
    [Fact]
    public async Task SendVerificationCodeAsync_ShouldPostNormalizedOtpRequest()
    {
        var handler =
            new RecordingHandler(
                """{"code":"4901","SMSID":"1","Cost":"1"}""");

        using var httpClient =
            new HttpClient(handler);

        var sender =
            new SmsMisrSmsSender(
                httpClient,
                Options.Create(
                    new SmsMisrOptions
                    {
                        Username = "test-user",
                        Password = "test-password",
                        Sender = "test-sender",
                        Template = "test-template",
                        Environment = 2,
                        BaseUrl = "https://smsmisr.com"
                    }));

        await sender.SendVerificationCodeAsync(
            "+201001234567",
            "123456",
            VerificationPurpose.VerifyPhone,
            CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Equal(
            HttpMethod.Post,
            handler.Request.Method);
        Assert.Equal(
            "https://smsmisr.com/api/OTP/",
            handler.Request.RequestUri?.ToString());

        Dictionary<string, string> form =
            ParseForm(handler.Body);

        Assert.Equal("2", form["environment"]);
        Assert.Equal("test-user", form["username"]);
        Assert.Equal("test-password", form["password"]);
        Assert.Equal("test-sender", form["sender"]);
        Assert.Equal("201001234567", form["mobile"]);
        Assert.Equal("test-template", form["template"]);
        Assert.Equal("123456", form["otp"]);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_ShouldPostFreeTextSmsWhenTemplateMissing()
    {
        var handler =
            new RecordingHandler(
                """{"code":"1901","SMSID":"1","Cost":"1"}""");

        using var httpClient =
            new HttpClient(handler);

        var sender =
            new SmsMisrSmsSender(
                httpClient,
                Options.Create(
                    new SmsMisrOptions
                    {
                        Username = "test-user",
                        Password = "test-password",
                        Sender = "test-sender",
                        Environment = 2,
                        BaseUrl = "https://smsmisr.com"
                    }));

        await sender.SendVerificationCodeAsync(
            "+201001234567",
            "123456",
            VerificationPurpose.VerifyPhone,
            CancellationToken.None);

        Assert.Equal(
            "https://smsmisr.com/api/SMS/",
            handler.Request?.RequestUri?.ToString());

        Dictionary<string, string> form =
            ParseForm(handler.Body);

        Assert.Equal("2", form["environment"]);
        Assert.Equal("201001234567", form["mobile"]);
        Assert.Equal("3", form["language"]);
        Assert.Contains("123456", form["message"]);
        Assert.False(form.ContainsKey("template"));
        Assert.False(form.ContainsKey("otp"));
    }

    private static Dictionary<string, string> ParseForm(
        string body)
    {
        return body
            .Split(
                '&',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(
                '=',
                2))
            .ToDictionary(
                part => Uri.UnescapeDataString(
                    part[0].Replace('+', ' ')),
                part => part.Length > 1
                    ? Uri.UnescapeDataString(
                        part[1].Replace('+', ' '))
                    : string.Empty);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(
            string responseBody)
        {
            _responseBody = responseBody;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;

            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(
                    cancellationToken);
            }

            return new HttpResponseMessage(
                HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}