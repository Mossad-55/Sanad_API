using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Families.Domain.Sos;
using Sanad.Modules.Identity.Application.Authentication.Login;
using Sanad.Modules.Identity.Application.Authentication.Refresh;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Application.Authentication.Verification;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// FUR-01 literal contract evidence for documented auth request/response shapes and
/// shared enum/dictionary/ID conventions, serialized with the production MVC JSON
/// options. Synthetic values only; no real tokens, PII, OTP sends or providers.
/// <see cref="PortalLiteralExampleConformanceTests"/> bridges these DTO truths to
/// the actual published Markdown/Postman literals, so the guide examples below
/// are verified against runtime serialization rather than restated from memory.
/// </summary>
public sealed class PortalAuthExampleContractTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public PortalAuthExampleContractTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void RegisterRequest_SerializesContractedPropertyNames()
    {
        var request = new RegisterRequest(
            "أحمد حسن", "Ahmed Hassan", "ahmed@example.test", "+201001234567", "Password1ab", AccountType.Family);
        using JsonDocument payload = JsonDocument.Parse(Serialize(request));
        JsonElement json = payload.RootElement;

        Assert.Equal("أحمد حسن", json.GetProperty("arabicFullName").GetString());
        Assert.Equal("Ahmed Hassan", json.GetProperty("englishFullName").GetString());
        Assert.Equal("ahmed@example.test", json.GetProperty("email").GetString());
        Assert.Equal("+201001234567", json.GetProperty("phoneNumber").GetString());
        Assert.Equal(1, json.GetProperty("accountType").GetInt32());
        Assert.False(json.TryGetProperty("firstName", out _));
        Assert.False(json.TryGetProperty("lastName", out _));
        Assert.False(json.TryGetProperty("phone", out _));
    }

    [Fact]
    public void VerifyAndResendRequests_UseVerificationRequestIdPlusCode()
    {
        Guid id = Guid.Parse("0198e2c2-4444-7777-8888-000000000001");
        using JsonDocument verify = JsonDocument.Parse(Serialize(new VerifyOtpRequest(id, "123456")));
        Assert.Equal(id.ToString("D"), verify.RootElement.GetProperty("verificationRequestId").GetString());
        Assert.Equal("123456", verify.RootElement.GetProperty("code").GetString());
        Assert.False(verify.RootElement.TryGetProperty("email", out _));
        Assert.False(verify.RootElement.TryGetProperty("purpose", out _));

        using JsonDocument resend = JsonDocument.Parse(Serialize(new ResendOtpRequest(id)));
        Assert.Equal(id.ToString("D"), resend.RootElement.GetProperty("verificationRequestId").GetString());
        Assert.False(resend.RootElement.TryGetProperty("email", out _));
        Assert.False(resend.RootElement.TryGetProperty("purpose", out _));
    }

    [Fact]
    public void RegisterResponse_SerializesWrapperIds()
    {
        var sample = new RegisterUserResponse(
            new UserId(Guid.Parse("0198e2c2-4444-7777-8888-000000000001")),
            new VerificationRequestId(Guid.Parse("0198e2c2-4444-7777-8888-000000000002")),
            new VerificationRequestId(Guid.Parse("0198e2c2-4444-7777-8888-000000000003")));
        using JsonDocument payload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = payload.RootElement;

        Assert.Equal(
            "0198e2c2-4444-7777-8888-000000000001",
            json.GetProperty("userId").GetProperty("value").GetString());
        Assert.Equal(
            "0198e2c2-4444-7777-8888-000000000002",
            json.GetProperty("emailVerificationRequestId").GetProperty("value").GetString());
        Assert.Equal(
            "0198e2c2-4444-7777-8888-000000000003",
            json.GetProperty("phoneVerificationRequestId").GetProperty("value").GetString());
    }

    [Fact]
    public void VerifyResponse_PreservesLiteralAttemptesRemainingSpelling()
    {
        var sample = new VerifyOtpResponse(
            new UserId(Guid.Parse("0198e2c2-4444-7777-8888-000000000001")), true, true, true, 5);
        using JsonDocument payload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = payload.RootElement;

        Assert.Equal(5, json.GetProperty("attemptesRemaining").GetInt32());
        Assert.False(json.TryGetProperty("attemptsRemaining", out _));
        Assert.True(json.GetProperty("emailVerified").GetBoolean());
        Assert.True(json.GetProperty("normalAccessAllowed").GetBoolean());
    }

    [Fact]
    public void ResendResponse_ReturnsReplacementRequestIdAndExpiry()
    {
        Guid replacement = Guid.Parse("0198e2c2-4444-7777-8888-000000000004");
        var sample = new ResendOtpResponse(
            new VerificationRequestId(replacement), new DateTime(2026, 10, 16, 12, 15, 0, DateTimeKind.Utc));
        using JsonDocument payload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = payload.RootElement;

        Assert.Equal(
            replacement.ToString("D"),
            json.GetProperty("verificationRequestId").GetProperty("value").GetString());
        Assert.Equal(
            "2026-10-16T12:15:00Z",
            json.GetProperty("expiresOnUtc").GetString());
    }

    [Fact]
    public void RestrictedLogin_HasNoVerificationRequestIdsOrSessionValues()
    {
        var restricted = new LoginResponse(
            new UserId(Guid.Parse("0198e2c2-4444-7777-8888-000000000001")),
            AuthAccessType.RestrictedVerification,
            "synthetic-access-token",
            new DateTime(2026, 10, 16, 12, 15, 0, DateTimeKind.Utc),
            null, null, null, false, true);
        using JsonDocument payload = JsonDocument.Parse(Serialize(restricted));
        JsonElement json = payload.RootElement;

        Assert.Equal(JsonValueKind.Null, json.GetProperty("refreshToken").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("deviceSessionId").ValueKind);
        Assert.False(json.GetProperty("emailVerified").GetBoolean());
        Assert.False(json.TryGetProperty("emailVerificationRequestId", out _));
        Assert.False(json.TryGetProperty("phoneVerificationRequestId", out _));
    }

    [Fact]
    public void ActiveLoginAndRefresh_SerializeNullableSessionValues()
    {
        Guid session = Guid.Parse("0198e2c2-4444-7777-8888-000000000010");
        var active = new LoginResponse(
            new UserId(Guid.Parse("0198e2c2-4444-7777-8888-000000000001")),
            AuthAccessType.Normal,
            "synthetic-access-token",
            new DateTime(2026, 10, 16, 12, 15, 0, DateTimeKind.Utc),
            "synthetic-refresh-token",
            new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc),
            new DeviceSessionId(session), true, true);
        using JsonDocument payload = JsonDocument.Parse(Serialize(active));

        Assert.Equal("synthetic-refresh-token", payload.RootElement.GetProperty("refreshToken").GetString());
        Assert.Equal(
            session.ToString("D"),
            payload.RootElement.GetProperty("deviceSessionId").GetProperty("value").GetString());

        var refresh = new RefreshTokenResponse(
            new DeviceSessionId(session),
            "synthetic-access-token",
            new DateTime(2026, 10, 16, 12, 15, 0, DateTimeKind.Utc),
            "synthetic-refresh-token",
            new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc));
        using JsonDocument refreshPayload = JsonDocument.Parse(Serialize(refresh));

        // Headers such as X-Device-Session-Id use the inner GUID, not a JSON wrapper.
        Assert.Equal(
            session.ToString("D"),
            refreshPayload.RootElement.GetProperty("deviceSessionId").GetProperty("value").GetString());
    }

    [Fact]
    public void HelpAggregate_SerializesAsDictionaryWithoutEnvelope()
    {
        var aggregate = new Dictionary<ElderlyHelpRequestStatus, int>
        {
            [ElderlyHelpRequestStatus.Pending] = 4,
            [ElderlyHelpRequestStatus.Resolved] = 9
        };
        using JsonDocument payload = JsonDocument.Parse(Serialize(aggregate));
        JsonElement json = payload.RootElement;

        // Proven literal truth with production MVC options: enum dictionary keys
        // serialize as enum NAMES ("Pending"), not numeric strings. Document the
        // names, never a guessed statusCounts array/envelope.
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.Equal(4, json.GetProperty("Pending").GetInt32());
        Assert.Equal(9, json.GetProperty("Resolved").GetInt32());
        Assert.False(json.TryGetProperty("statusCounts", out _));
        Assert.False(json.TryGetProperty("1", out _));
    }

    [Fact]
    public void NumericEnums_KeepDocumentedValues()
    {
        Assert.Equal(1, (int)UserStatus.PendingVerification);
        Assert.Equal(4, (int)UserStatus.Blocked);
        Assert.Equal(9, (int)AccountType.FinanceAdmin);
        Assert.Equal(0, (int)DevicePlatform.Unknown);
        Assert.Equal(3, (int)DevicePlatform.Web);
        Assert.Equal(0, (int)DayOfWeek.Sunday);
        Assert.Equal(1, (int)DayOfWeek.Monday);
        Assert.Equal(DayOfWeek.Monday, (DayOfWeek)1);
        Assert.Equal(DayOfWeek.Saturday, (DayOfWeek)6);
        Assert.Equal(2, (int)AdminUserActionType.Reactivated);
    }

    [Fact]
    public void BookingEnums_RemainDistinct()
    {
        Assert.Equal(10, Enum.GetValues<BookingStatus>().Length);
        Assert.Equal(10, (int)BookingStatus.Expired);
        Assert.Equal(9, Enum.GetValues<CareHomeBookingStatus>().Length);
        Assert.Equal(9, (int)CareHomeBookingStatus.Refunded);
        Assert.Equal(2, (int)CareHomeBookingStatus.PaidAwaitingDecision);
    }

    [Fact]
    public void SosHistoryAction_IsNotSosStatus()
    {
        Assert.Equal(4, Enum.GetValues<ElderlySosHistoryAction>().Length);
        Assert.Equal(1, (int)ElderlySosHistoryAction.Created);
        Assert.NotEqual(
            Enum.GetValues<ElderlySosHistoryAction>().Select(v => v.ToString()).OrderBy(x => x).ToArray(),
            Enum.GetValues<ElderlySosStatus>().Select(v => v.ToString()).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void WrapperIds_KeepValueShape_PlainGuidsStayPlain()
    {
        var wrapper = new UserId(Guid.Parse("0198e2c2-4444-7777-8888-000000000001"));
        using JsonDocument payload = JsonDocument.Parse(Serialize(wrapper));
        Assert.Equal(
            "0198e2c2-4444-7777-8888-000000000001",
            payload.RootElement.GetProperty("value").GetString());

        using JsonDocument plain = JsonDocument.Parse(Serialize(new VerifyOtpRequest(
            Guid.Parse("0198e2c2-4444-7777-8888-000000000002"), "123456")));
        Assert.Equal(
            "0198e2c2-4444-7777-8888-000000000002",
            plain.RootElement.GetProperty("verificationRequestId").GetString());
    }

    private string Serialize<T>(T value)
    {
        JsonSerializerOptions options = _host.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
            .Value.JsonSerializerOptions;
        return JsonSerializer.Serialize(value, options);
    }
}

/// <summary>
/// R3 bridge: reads the actual published literals (three auth guides plus the
/// seven edited Auth Postman response bodies) and checks them against real DTO
/// binding/serialization with the production MVC JSON options. Request examples
/// must bind to the actual request DTOs; response examples must match the
/// serialized shape, nullability and wrapper-ID structure of the actual
/// response DTOs. Sample GUIDs/tokens/dates may differ; only contract-relevant
/// shape is asserted. No provider, database or live API is involved.
/// </summary>
public sealed class PortalLiteralExampleConformanceTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public PortalLiteralExampleConformanceTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void RegistrationGuide_RequestExamples_BindActualDtos()
    {
        List<JsonElement> blocks = JsonBlocks("docs/auth/registration-and-verification.md");

        JsonElement register = blocks.Single(b =>
            b.TryGetProperty("arabicFullName", out _));
        RegisterRequest? bound = Deserialize<RegisterRequest>(register.GetRawText());
        Assert.NotNull(bound);
        Assert.False(string.IsNullOrWhiteSpace(bound.ArabicFullName));
        Assert.False(string.IsNullOrWhiteSpace(bound.EnglishFullName));
        Assert.False(string.IsNullOrWhiteSpace(bound.Email));
        Assert.False(string.IsNullOrWhiteSpace(bound.PhoneNumber));
        Assert.False(string.IsNullOrWhiteSpace(bound.Password));
        Assert.True(Enum.IsDefined(bound.AccountType));
        Assert.DoesNotContain(blocks, b =>
            b.TryGetProperty("firstName", out _) || b.TryGetProperty("phone", out _));

        JsonElement verify = blocks.Single(b =>
            b.TryGetProperty("code", out _));
        VerifyOtpRequest? verifyBound = Deserialize<VerifyOtpRequest>(verify.GetRawText());
        Assert.NotNull(verifyBound);
        Assert.NotEqual(Guid.Empty, verifyBound.VerificationRequestId);
        Assert.False(string.IsNullOrWhiteSpace(verifyBound.Code));
        Assert.False(verify.TryGetProperty("purpose", out _));
        Assert.False(verify.TryGetProperty("email", out _));

        JsonElement resend = blocks.Single(b =>
            b.TryGetProperty("verificationRequestId", out _) &&
            !b.TryGetProperty("code", out _) &&
            !b.TryGetProperty("expiresOnUtc", out _));
        ResendOtpRequest? resendBound = Deserialize<ResendOtpRequest>(resend.GetRawText());
        Assert.NotNull(resendBound);
        Assert.NotEqual(Guid.Empty, resendBound.VerificationRequestId);
    }

    [Fact]
    public void RegistrationGuide_ResponseExamples_MatchSerializedShapes()
    {
        List<JsonElement> blocks = JsonBlocks("docs/auth/registration-and-verification.md");

        JsonElement register = blocks.Single(b => b.TryGetProperty("emailVerificationRequestId", out _));
        Assert.True(MatchesSampleShape(register, Sample(new RegisterUserResponse(
            new UserId(Guid.NewGuid()), new VerificationRequestId(Guid.NewGuid()), new VerificationRequestId(Guid.NewGuid())))));

        JsonElement verify = blocks.Single(b => b.TryGetProperty("attemptesRemaining", out _));
        Assert.True(MatchesSampleShape(verify, Sample(new VerifyOtpResponse(
            new UserId(Guid.NewGuid()), true, true, true, 5))));
        Assert.False(verify.TryGetProperty("attemptsRemaining", out _));

        JsonElement resend = blocks.Single(b => b.TryGetProperty("expiresOnUtc", out _));
        Assert.True(MatchesSampleShape(resend, Sample(new ResendOtpResponse(
            new VerificationRequestId(Guid.NewGuid()), DateTime.UtcNow))));
    }

    [Fact]
    public void LoginGuide_Examples_BindAndMatchShapes()
    {
        List<JsonElement> blocks = JsonBlocks("docs/auth/email-password-login.md");

        JsonElement phone = blocks.Single(b =>
            b.TryGetProperty("identifier", out _) && b.TryGetProperty("devicePlatform", out _));
        LoginRequest? bound = Deserialize<LoginRequest>(phone.GetRawText());
        Assert.NotNull(bound);
        Assert.Equal("+201001234567", bound.LoginIdentifier);
        Assert.True(Enum.IsDefined(bound.DevicePlatform));

        JsonElement pending = blocks.Single(b =>
            b.TryGetProperty("accessType", out JsonElement access) && access.GetInt32() == 2);
        Assert.True(MatchesSampleShape(pending, Sample(RestrictedLoginSample())));
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("refreshToken").ValueKind);
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("deviceSessionId").ValueKind);
        Assert.False(pending.TryGetProperty("emailVerificationRequestId", out _));

        JsonElement active = blocks.Single(b =>
            b.TryGetProperty("accessType", out JsonElement access) && access.GetInt32() == 1);
        Assert.True(MatchesSampleShape(active, Sample(ActiveLoginSample())));
        Assert.True(active.GetProperty("deviceSessionId").TryGetProperty("value", out _));
    }

    [Fact]
    public void RefreshGuide_Examples_KeepPlainRequestAndWrapperResponse()
    {
        List<JsonElement> blocks = JsonBlocks("docs/auth/refresh-and-sessions.md");

        JsonElement request = blocks.Single(b =>
            b.TryGetProperty("refreshToken", out _) && !b.TryGetProperty("accessToken", out _));
        RefreshTokenRequest? bound = Deserialize<RefreshTokenRequest>(request.GetRawText());
        Assert.NotNull(bound);
        Assert.NotEqual(Guid.Empty, bound.DeviceSessionId);
        Assert.False(string.IsNullOrWhiteSpace(bound.RefreshToken));

        JsonElement response = blocks.Single(b => b.TryGetProperty("accessToken", out _));
        Assert.True(MatchesSampleShape(response, Sample(RefreshSample())));
        Assert.True(response.GetProperty("deviceSessionId").TryGetProperty("value", out JsonElement inner));
        Assert.True(Guid.TryParse(inner.GetString(), out _));
    }

    [Fact]
    public void PostmanSevenEditedBodies_MatchSerializedShapes()
    {
        using JsonDocument collection = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "docs/postman/Sanad.Auth.postman_collection.json")));
        var wanted = new (string Request, string Response, Func<object> Sample)[]
        {
            ("Register", "201 Created", () => new RegisterUserResponse(
                new UserId(Guid.NewGuid()), new VerificationRequestId(Guid.NewGuid()), new VerificationRequestId(Guid.NewGuid()))),
            ("Verify OTP", "200 Verified, not yet normal access", () => RestrictedVerifySample()),
            ("Verify OTP", "200 Both channels verified", () => new VerifyOtpResponse(
                new UserId(Guid.NewGuid()), true, true, true, 5)),
            ("Resend OTP", "200 Replaced", () => new ResendOtpResponse(
                new VerificationRequestId(Guid.NewGuid()), DateTime.UtcNow)),
            ("Login", "200 Normal access", () => ActiveLoginSample()),
            ("Login", "200 Restricted verification", () => RestrictedLoginSample()),
            ("Refresh", "200 Rotated", () => RefreshSample()),
        };

        int matched = 0;
        void Walk(JsonElement items)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.TryGetProperty("item", out JsonElement nested))
                {
                    Walk(nested);
                    continue;
                }

                foreach ((string request, string response, Func<object> sample) in wanted)
                {
                    if (item.GetProperty("name").GetString() != request)
                        continue;
                    foreach (JsonElement saved in item.GetProperty("response").EnumerateArray())
                    {
                        if (saved.GetProperty("name").GetString() != response)
                            continue;
                        using JsonDocument body = JsonDocument.Parse(saved.GetProperty("body").GetString()!);
                        Assert.True(
                            MatchesSampleShape(body.RootElement, Sample(sample())),
                            $"Postman {request} / {response} drifts from serialized shape.");
                        matched++;
                    }
                }
            }
        }

        Walk(collection.RootElement.GetProperty("item"));
        Assert.Equal(7, matched);
    }

    [Fact]
    public void SupersededPlainIdExamples_FailShapeGuard()
    {
        // Pre-FUR-01 literals (superseded 2026-10-16): plain GUID strings where
        // the runtime returns `{ "value": guid }` wrappers. They must not match.
        const string oldRegister = """{"userId":"018f0000-0000-7000-8000-000000000001","emailVerificationRequestId":"018f0000-0000-7000-8000-000000000002","phoneVerificationRequestId":"018f0000-0000-7000-8000-000000000003"}""";
        const string oldLogin = """{"userId":"018f0000-0000-7000-8000-000000000001","accessType":1,"deviceSessionId":"018f0000-0000-7000-8000-000000000010"}""";

        using JsonDocument register = JsonDocument.Parse(oldRegister);
        Assert.False(MatchesSampleShape(register.RootElement, Sample(new RegisterUserResponse(
            new UserId(Guid.NewGuid()), new VerificationRequestId(Guid.NewGuid()), new VerificationRequestId(Guid.NewGuid())))));

        using JsonDocument login = JsonDocument.Parse(oldLogin);
        Assert.False(MatchesSampleShape(login.RootElement, Sample(ActiveLoginSample())));
    }

    [Fact]
    public void ActivationProse_StatesBothChannelsRule()
    {
        // Documentation-string check only: the handler rule itself
        // (VerifyOtpCommandHandler checks both flags after either purpose) is
        // proven by source inspection, not executed here.
        string text = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "docs/auth/registration-and-verification.md"));
        Assert.Contains("both", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activate", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email verification does not activate the user", text, StringComparison.Ordinal);
    }

    private static LoginResponse RestrictedLoginSample() => new(
        new UserId(Guid.NewGuid()), AuthAccessType.RestrictedVerification,
        "synthetic", DateTime.UtcNow, null, null, null, false, true);

    private static LoginResponse ActiveLoginSample() => new(
        new UserId(Guid.NewGuid()), AuthAccessType.Normal,
        "synthetic", DateTime.UtcNow, "synthetic-refresh", DateTime.UtcNow,
        new DeviceSessionId(Guid.NewGuid()), true, true);

    private static VerifyOtpResponse RestrictedVerifySample() => new(
        new UserId(Guid.NewGuid()), true, false, false, 4);

    private static RefreshTokenResponse RefreshSample() => new(
        new DeviceSessionId(Guid.NewGuid()), "synthetic", DateTime.UtcNow,
        "synthetic-refresh", DateTime.UtcNow);

    private JsonElement Sample<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, MvcOptions());
        return JsonDocument.Parse(json).RootElement;
    }

    private T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, MvcOptions());

    private JsonSerializerOptions MvcOptions() => _host.Services
        .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
        .Value.JsonSerializerOptions;

    private static bool MatchesSampleShape(JsonElement example, JsonElement sample)
    {
        if (example.ValueKind != sample.ValueKind)
            return false;
        if (example.ValueKind != JsonValueKind.Object)
            return true;
        foreach (JsonProperty property in sample.EnumerateObject())
        {
            if (!example.TryGetProperty(property.Name, out JsonElement actual))
                return false;
            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("value", out _))
            {
                if (actual.ValueKind != JsonValueKind.Object ||
                    !actual.TryGetProperty("value", out _))
                    return false;
            }
            else if (property.Value.ValueKind == JsonValueKind.Null)
            {
                if (actual.ValueKind != JsonValueKind.Null)
                    return false;
            }
            else if (ShapeKind(actual) != ShapeKind(property.Value))
            {
                return false;
            }
        }

        foreach (JsonProperty property in example.EnumerateObject())
        {
            if (!sample.TryGetProperty(property.Name, out _))
                return false;
        }

        return true;
    }

    private static string ShapeKind(JsonElement element) => element.ValueKind switch
    {
        // System.Text.Json splits booleans into True/False kinds; shape cares
        // about the boolean type, never the sample value.
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => element.ValueKind.ToString()
    };

    private static string RepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        for (int depth = 0; depth < 10 && current is not null; depth++)
        {
            if (File.Exists(Path.Combine(current, "docs/auth/registration-and-verification.md")) &&
                File.Exists(Path.Combine(current, "docs/postman/Sanad.Auth.postman_collection.json")))
                return current;
            current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException("Repository root with docs/auth and docs/postman was not found.");
    }

    private static List<JsonElement> JsonBlocks(string relativePath)
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), relativePath));
        var blocks = new List<JsonElement>();
        List<string>? current = null;
        foreach (string line in lines)
        {
            if (current is null)
            {
                if (line.Trim() == "```json")
                    current = new List<string>();
            }
            else if (line.Trim() == "```")
            {
                blocks.Add(JsonDocument.Parse(string.Join("\n", current)).RootElement);
                current = null;
            }
            else
            {
                current.Add(line);
            }
        }

        Assert.Null(current);
        return blocks;
    }
}
