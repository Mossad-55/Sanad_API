using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Sanad.Modules.Identity.Infrastructure.Messaging;

public sealed class SmsMisrSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly SmsMisrOptions _options;

    public SmsMisrSmsSender(
        HttpClient httpClient,
        IOptions<SmsMisrOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SendVerificationCodeAsync(
    string phoneNumber,
    string code,
    VerificationPurpose purpose,
    CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool useOtpApi = _options.UseOtpApi;

        using var content = new FormUrlEncodedContent(
            CreateForm(
                phoneNumber,
                code,
                useOtpApi));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            GetEndpoint(useOtpApi))
        {
            Content = content
        };

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        using HttpResponseMessage response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        string body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        string expectedCode = useOtpApi
            ? SmsMisrOptions.OtpSuccessCode
            : SmsMisrOptions.SmsSuccessCode;

        string? providerCode = ReadProviderCode(body);

        if (!response.IsSuccessStatusCode ||
            providerCode != expectedCode)
        {
            throw new InvalidOperationException(
                $"SMS Misr OTP delivery failed. HTTP {(int)response.StatusCode}. Provider code: {providerCode ?? "none"}.");
        }
    }

    private List<KeyValuePair<string, string>> CreateForm(
        string phoneNumber,
        string code,
        bool useOtpApi)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("environment", _options.Environment.ToString()),
            new("username", _options.Username),
            new("password", _options.Password),
            new("sender", _options.Sender),
            new("mobile", NormalizeMobile(phoneNumber))
        };

        if (useOtpApi)
        {
            form.Add(new("template", _options.Template));
            form.Add(new("otp", code));
            return form;
        }

        form.Add(new("language", "1"));
        form.Add(new("message", CreateSmsMessage(code)));
        return form;
    }

    private Uri GetEndpoint(
        bool useOtpApi)
    {
        string baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://smsmisr.com"
            : _options.BaseUrl.TrimEnd('/');

        string path = useOtpApi
            ? "/api/OTP/"
            : "/api/SMS/";

        return new Uri(
            $"{baseUrl}{path}");
    }

    private static string CreateSmsMessage(
        string code)
    {
        return
            $"Your Sanad Care code is {code}. It will expire after 5 mins.";
    }

    private static string NormalizeMobile(
        string phoneNumber)
    {
        return phoneNumber.StartsWith(
            '+')
            ? phoneNumber[1..]
            : phoneNumber;
    }

    private static string? ReadProviderCode(
        string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty(
                    "code",
                    out JsonElement code))
            {
                return null;
            }

            return code.ValueKind switch
            {
                JsonValueKind.String =>
                    code.GetString(),
                JsonValueKind.Number =>
                    code.GetRawText(),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}