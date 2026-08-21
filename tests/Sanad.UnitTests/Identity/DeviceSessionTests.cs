using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions.Events;

namespace Sanad.UnitTests.Identity;

public sealed class DeviceSessionTests
{
    [Fact]
    public void Create_ShouldCreateActiveSession()
    {
        DateTime createdOnUtc =
            CreateUtcDateTime();

        DeviceSession session =
            CreateSession(createdOnUtc);

        Assert.NotEqual(
            DeviceSessionId.Empty,
            session.Id);

        Assert.False(session.IsRevoked);
        Assert.False(session.HasReuseDetection);
        Assert.Equal(0, session.RotationCount);
        Assert.True(session.IsActive(createdOnUtc));

        DeviceSessionCreatedDomainEvent domainEvent =
            Assert.Single(
                session.DomainEvents
                    .OfType<
                        DeviceSessionCreatedDomainEvent>());

        Assert.Equal(session.Id, domainEvent.DeviceSessionId);
        Assert.Equal(session.UserId, domainEvent.UserId);
    }

    [Fact]
    public void Create_ShouldRejectInvalidExpiration()
    {
        DateTime createdOnUtc =
            CreateUtcDateTime();

        Assert.Throws<DomainException>(
            () => DeviceSession.Create(
                UserId.New(),
                "iPhone 16",
                DevicePlatform.iOS,
                "1.0.0",
                "refresh-token-hash",
                createdOnUtc,
                createdOnUtc));
    }

    [Fact]
    public void RotateRefreshToken_ShouldReplaceHashAndRaiseEvent()
    {
        DateTime createdOnUtc =
            CreateUtcDateTime();

        DeviceSession session =
            CreateSession(createdOnUtc);

        session.ClearDomainEvents();

        DateTime rotatedOnUtc =
            createdOnUtc.AddDays(1);

        DateTime newExpiry =
            rotatedOnUtc.AddDays(30);

        session.RotateRefreshToken(
            "new-refresh-token-hash",
            newExpiry,
            rotatedOnUtc);

        Assert.Equal(
            "new-refresh-token-hash",
            session.RefreshTokenHash);

        Assert.Equal(1, session.RotationCount);
        Assert.Equal(rotatedOnUtc, session.LastRotatedOnUtc);
        Assert.Equal(newExpiry, session.ExpiresOnUtc);

        DeviceSessionRefreshTokenRotatedDomainEvent domainEvent =
            Assert.Single(
                session.DomainEvents
                    .OfType<
                        DeviceSessionRefreshTokenRotatedDomainEvent>());

        Assert.Equal(1, domainEvent.RotationCount);
        Assert.Equal(newExpiry, domainEvent.ExpiresOnUtc);
    }

    [Fact]
    public void RotateRefreshToken_ShouldRejectRevokedSession()
    {
        DateTime createdOnUtc =
            CreateUtcDateTime();

        DeviceSession session =
            CreateSession(createdOnUtc);

        session.Revoke(
            "User logged out.",
            createdOnUtc.AddMinutes(1));

        Assert.Throws<DomainException>(
            () => session.RotateRefreshToken(
                "new-hash",
                createdOnUtc.AddDays(30),
                createdOnUtc.AddMinutes(2)));
    }

    [Fact]
    public void RotateRefreshToken_ShouldRejectExpiredSession()
    {
        DateTime createdOnUtc =
            CreateUtcDateTime();

        DeviceSession session =
            CreateSession(createdOnUtc);

        DateTime afterExpiry =
            session.ExpiresOnUtc.AddSeconds(1);

        Assert.Throws<DomainException>(
            () => session.RotateRefreshToken(
                "new-hash",
                afterExpiry.AddDays(30),
                afterExpiry));
    }

    [Fact]
    public void RegisterRefreshTokenReuse_ShouldRevokeSessionAndRaiseEvent()
    {
        DateTime createdOnUtc =
            CreateUtcDateTime();

        DeviceSession session =
            CreateSession(createdOnUtc);

        session.ClearDomainEvents();

        DateTime detectedOnUtc =
            createdOnUtc.AddMinutes(5);

        session.RegisterRefreshTokenReuse(
            detectedOnUtc);

        Assert.True(session.IsRevoked);
        Assert.True(session.HasReuseDetection);
        Assert.Equal(detectedOnUtc, session.ReuseDetectedOnUtc);
        Assert.Equal(detectedOnUtc, session.RevokedOnUtc);

        DeviceSessionRefreshTokenReuseDetectedDomainEvent domainEvent =
            Assert.Single(
                session.DomainEvents
                    .OfType<
                        DeviceSessionRefreshTokenReuseDetectedDomainEvent>());

        Assert.Equal(session.Id, domainEvent.DeviceSessionId);
        Assert.Equal(session.UserId, domainEvent.UserId);
    }

    [Fact]
    public void RegisterRefreshTokenReuse_ShouldBeIdempotent()
    {
        DeviceSession session =
            CreateSession(
                CreateUtcDateTime());

        DateTime firstDetection =
            CreateUtcDateTime()
                .AddMinutes(5);

        session.RegisterRefreshTokenReuse(
            firstDetection);

        session.ClearDomainEvents();

        session.RegisterRefreshTokenReuse(
            firstDetection.AddMinutes(1));

        Assert.Equal(
            firstDetection,
            session.ReuseDetectedOnUtc);

        Assert.Empty(session.DomainEvents);
    }

    [Fact]
    public void Revoke_ShouldStoreReasonAndBeIdempotent()
    {
        DeviceSession session =
            CreateSession(
                CreateUtcDateTime());

        DateTime revokedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(1);

        session.Revoke(
            "  User logged out.  ",
            revokedOnUtc);

        Assert.True(session.IsRevoked);
        Assert.Equal(revokedOnUtc, session.RevokedOnUtc);
        Assert.Equal("User logged out.", session.RevocationReason);

        session.ClearDomainEvents();

        session.Revoke(
            "Another reason.",
            revokedOnUtc.AddMinutes(1));

        Assert.Equal(revokedOnUtc, session.RevokedOnUtc);
        Assert.Equal("User logged out.", session.RevocationReason);
        Assert.Empty(session.DomainEvents);
    }

    [Fact]
    public void IsExpired_ShouldUseExactBoundary()
    {
        DeviceSession session =
            CreateSession(
                CreateUtcDateTime());

        Assert.False(
            session.IsExpired(
                session.ExpiresOnUtc.AddSeconds(-1)));

        Assert.True(
            session.IsExpired(
                session.ExpiresOnUtc));
    }

    private static DeviceSession CreateSession(
        DateTime createdOnUtc)
    {
        return DeviceSession.Create(
            UserId.New(),
            "iPhone 16",
            DevicePlatform.iOS,
            "1.0.0",
            "refresh-token-hash",
            createdOnUtc,
            createdOnUtc.AddDays(30));
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);
    }
}