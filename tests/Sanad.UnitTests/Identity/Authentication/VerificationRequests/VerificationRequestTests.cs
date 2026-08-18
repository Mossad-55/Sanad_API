using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests.Events;

namespace Sanad.UnitTests.Identity.Authentication.VerificationRequests;

public sealed class VerificationRequestTests
{
    [Fact]
    public void Create_ShouldUseProvidedCreationAndExpirationTimes()
    {
        DateTime createdOnUtc = new(
            2026,
            8,
            19,
            12,
            0,
            0,
            DateTimeKind.Utc);

        DateTime expiresOnUtc =
            createdOnUtc.AddMinutes(5);

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                expiresOnUtc);

        Assert.Equal(
            createdOnUtc,
            request.CreatedOnUtc);

        Assert.Equal(
            expiresOnUtc,
            request.ExpiresOnUtc);
    }

    [Fact]
    public void Create_ShouldInitializePendingRequest()
    {
        DateTime createdOnUtc = CreateUtcDateTime();
        DateTime expiresOnUtc =
            createdOnUtc.AddMinutes(5);

        UserId userId = UserId.New();

        VerificationRequest request =
            VerificationRequest.Create(
                userId,
                "  user@example.com  ",
                "hashed-otp",
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                createdOnUtc,
                expiresOnUtc);

        Assert.NotEqual(
            VerificationRequestId.Empty,
            request.Id);

        Assert.Equal(userId, request.UserId);
        Assert.Equal(
            "user@example.com",
            request.Target);

        Assert.Equal(
            "hashed-otp",
            request.OtpHash);

        Assert.Equal(
            VerificationChannel.Email,
            request.Channel);

        Assert.Equal(
            VerificationPurpose.VerifyEmail,
            request.Purpose);

        Assert.Equal(
            VerificationStatus.Pending,
            request.Status);

        Assert.Equal(0, request.Attempts);
        Assert.Equal(5, request.MaxAttempts);
        Assert.Null(request.VerifiedOnUtc);
        Assert.Null(request.InvalidatedOnUtc);
    }

    [Fact]
    public void Create_ShouldRaiseCreatedDomainEvent()
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                createdOnUtc.AddMinutes(5));

        VerificationRequestCreatedDomainEvent domainEvent =
            Assert.Single(
                request.DomainEvents
                    .OfType<VerificationRequestCreatedDomainEvent>());

        Assert.Equal(
            request.Id,
            domainEvent.VerificationRequestId);
    }

    [Theory]
    [InlineData(null, "hashed-otp")]
    [InlineData("", "hashed-otp")]
    [InlineData("   ", "hashed-otp")]
    [InlineData("user@example.com", null)]
    [InlineData("user@example.com", "")]
    [InlineData("user@example.com", "   ")]
    public void Create_ShouldRejectMissingTargetOrOtpHash(
        string? target,
        string? otpHash)
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        Assert.Throws<DomainException>(
            () => VerificationRequest.Create(
                UserId.New(),
                target!,
                otpHash!,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                createdOnUtc,
                createdOnUtc.AddMinutes(5)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Create_ShouldRejectNonUtcCreationTime(
        DateTimeKind dateTimeKind)
    {
        DateTime createdOnUtc = DateTime.SpecifyKind(
            new DateTime(2026, 8, 19, 12, 0, 0),
            dateTimeKind);

        DateTime expiresOnUtc = new(
            2026,
            8,
            19,
            12,
            5,
            0,
            DateTimeKind.Utc);

        Assert.Throws<DomainException>(
            () => CreateRequest(
                createdOnUtc,
                expiresOnUtc));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Create_ShouldRejectNonUtcExpirationTime(
        DateTimeKind dateTimeKind)
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        DateTime expiresOnUtc = DateTime.SpecifyKind(
            new DateTime(2026, 8, 19, 12, 5, 0),
            dateTimeKind);

        Assert.Throws<DomainException>(
            () => CreateRequest(
                createdOnUtc,
                expiresOnUtc));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldRejectInvalidExpirationOrder(
        int expirationOffsetMinutes)
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        DateTime expiresOnUtc =
            createdOnUtc.AddMinutes(
                expirationOffsetMinutes);

        Assert.Throws<DomainException>(
            () => CreateRequest(
                createdOnUtc,
                expiresOnUtc));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void IsExpired_ShouldReturnExpectedResult(
        int offsetFromExpirationSeconds,
        bool expectedResult)
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        DateTime expiresOnUtc =
            createdOnUtc.AddMinutes(5);

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                expiresOnUtc);

        DateTime currentTimeUtc =
            expiresOnUtc.AddSeconds(
                offsetFromExpirationSeconds);

        bool result =
            request.IsExpired(currentTimeUtc);

        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void IsExpired_ShouldRejectNonUtcCurrentTime(
        DateTimeKind dateTimeKind)
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        DateTime expiresOnUtc =
            createdOnUtc.AddMinutes(5);

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                expiresOnUtc);

        DateTime invalidCurrentTime =
            DateTime.SpecifyKind(
                expiresOnUtc,
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => request.IsExpired(
                invalidCurrentTime));
    }

    [Fact]
    public void Verify_ShouldMarkPendingRequestAsVerified()
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        DateTime expiresOnUtc =
            createdOnUtc.AddMinutes(5);

        DateTime verifiedOnUtc =
            createdOnUtc.AddMinutes(2);

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                expiresOnUtc);

        request.Verify(verifiedOnUtc);

        Assert.Equal(
            VerificationStatus.Verified,
            request.Status);

        Assert.Equal(
            verifiedOnUtc,
            request.VerifiedOnUtc);

        Assert.Null(request.InvalidatedOnUtc);
    }

    [Fact]
    public void Verify_ShouldRaiseVerifiedDomainEvent()
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                createdOnUtc.AddMinutes(5));

        request.Verify(
            createdOnUtc.AddMinutes(2));

        VerificationRequestVerifiedDomainEvent domainEvent =
            Assert.Single(
                request.DomainEvents
                    .OfType<VerificationRequestVerifiedDomainEvent>());

        Assert.Equal(
            request.Id,
            domainEvent.VerificationRequestId);
    }

    [Fact]
    public void Verify_ShouldRejectRequestAtExpirationBoundary()
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        DateTime expiresOnUtc =
            createdOnUtc.AddMinutes(5);

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                expiresOnUtc);

        Assert.Throws<DomainException>(
            () => request.Verify(expiresOnUtc));

        Assert.Equal(
            VerificationStatus.Pending,
            request.Status);

        Assert.Null(request.VerifiedOnUtc);

        Assert.Empty(
            request.DomainEvents
                .OfType<VerificationRequestVerifiedDomainEvent>());
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Verify_ShouldRejectNonUtcVerificationTime(
        DateTimeKind dateTimeKind)
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                createdOnUtc.AddMinutes(5));

        DateTime invalidVerificationTime =
            DateTime.SpecifyKind(
                createdOnUtc.AddMinutes(2),
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => request.Verify(
                invalidVerificationTime));

        Assert.Equal(
            VerificationStatus.Pending,
            request.Status);

        Assert.Null(request.VerifiedOnUtc);
    }

    [Fact]
    public void Verify_ShouldRejectSecondVerification()
    {
        DateTime createdOnUtc = CreateUtcDateTime();

        VerificationRequest request =
            CreateRequest(
                createdOnUtc,
                createdOnUtc.AddMinutes(5));

        DateTime firstVerificationTime =
            createdOnUtc.AddMinutes(1);

        request.Verify(firstVerificationTime);

        Assert.Throws<DomainException>(
            () => request.Verify(
                createdOnUtc.AddMinutes(2)));

        Assert.Equal(
            VerificationStatus.Verified,
            request.Status);

        Assert.Equal(
            firstVerificationTime,
            request.VerifiedOnUtc);

        Assert.Single(
            request.DomainEvents
                .OfType<VerificationRequestVerifiedDomainEvent>());
    }

    private static VerificationRequest CreateRequest(
        DateTime createdOnUtc,
        DateTime expiresOnUtc)
    {
        return VerificationRequest.Create(
            UserId.New(),
            "user@example.com",
            "hashed-otp",
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            createdOnUtc,
            expiresOnUtc);
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            19,
            12,
            0,
            0,
            DateTimeKind.Utc);
    }
}