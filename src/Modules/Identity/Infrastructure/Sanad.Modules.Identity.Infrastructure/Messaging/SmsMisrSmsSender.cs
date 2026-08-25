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

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>(
                "environment",
                _options.Environment.ToString()),
            new KeyValuePair<string, string>(
                "username",
                _options.Username),
            new KeyValuePair<string, string>(
                "password",
                _options.Password),
            new KeyValuePair<string, string>(
                "sender",
                _options.Sender),
            new KeyValuePair<string, string>(
                "mobile",
                NormalizeMobile(phoneNumber)),
            new KeyValuePair<string, string>(
                "template",
                _options.Template),
            new KeyValuePair<string, string>(
                "otp",
                code)
        ]);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            GetOtpEndpoint())
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

        if (!response.IsSuccessStatusCode ||
            !IsSuccessPayload(body))
        {
            throw new InvalidOperationException(
                "SMS Misr OTP delivery failed.");
        }
    }

    private Uri GetOtpEndpoint()
    {
        string baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://smsmisr.com"
            : _options.BaseUrl.TrimEnd('/');

        return new Uri(
            $"{baseUrl}/api/OTP/");
    }

    private static string NormalizeMobile(
        string phoneNumber)
    {
        return phoneNumber.StartsWith(
            '+')
            ? phoneNumber[1..]
            : phoneNumber;
    }

    private static bool IsSuccessPayload(
        string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty(
                       "code",
                       out JsonElement code) &&
                   code.GetString() == SmsMisrOptions.SuccessCode;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}