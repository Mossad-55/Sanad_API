using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.API.Seeding;

public sealed record TestUserSeedOptions
{
    public const string SectionName = "App:TestUserSeed";

    public bool Enabled { get; init; }

    public string Password { get; init; } = "Test-1234!";
}

// Opt-in idempotent fixture for end-to-end testing (TEST environments ONLY).
// Mirrors SuperAdminSeeder. Never enable against a live Paymob key or real users.
public sealed class TestUserDataSeeder
{
    private readonly IdentityDbContext _identityDbContext;
    private readonly IFamiliesDbContext _familiesDbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly TestUserSeedOptions _options;
    private readonly IConfiguration _configuration;

    public TestUserDataSeeder(
        IdentityDbContext identityDbContext,
        IFamiliesDbContext familiesDbContext,
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider,
        IOptions<TestUserSeedOptions> options,
        IConfiguration configuration)
    {
        _identityDbContext = identityDbContext;
        _familiesDbContext = familiesDbContext;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
        _options = options.Value;
        _configuration = configuration;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        string? paymobSecretKey =
            _configuration["Paymob:SecretKey"];

        if (!string.IsNullOrWhiteSpace(paymobSecretKey)
            && paymobSecretKey.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "App:TestUserSeed:Enabled must never be true against a LIVE Paymob key.");
        }

        DateTime utcNow = _dateTimeProvider.UtcNow;

        User owner = await EnsureUserAsync(
            arabicFullName: "مالك العيلة التجريبي",
            englishFullName: "Test Family Owner",
            email: "family.owner@test.sanad.local",
            phoneNumber: "+201000000001",
            AccountType.Family,
            utcNow,
            cancellationToken);

        User viewer = await EnsureUserAsync(
            arabicFullName: "فرد العيلة التجريبي",
            englishFullName: "Test Family Viewer",
            email: "family.viewer@test.sanad.local",
            phoneNumber: "+201000000002",
            AccountType.Family,
            utcNow,
            cancellationToken);

        User grandfatherUser = await EnsureElderlyUserAsync(
            arabicFullName: "الجد التجريبي",
            englishFullName: "Test Grandfather",
            phoneNumber: "+201000000005",
            Gender.Male,
            new DateOnly(1950, 1, 15),
            utcNow,
            cancellationToken);

        User grandmotherUser = await EnsureElderlyUserAsync(
            arabicFullName: "الجدة التجريبية",
            englishFullName: "Test Grandmother",
            phoneNumber: "+201000000006",
            Gender.Female,
            new DateOnly(1952, 3, 20),
            utcNow,
            cancellationToken);

        bool familyExists = await _familiesDbContext.Families
            .AnyAsync(f => f.OwnerUserId == owner.Id, cancellationToken);

        if (familyExists)
        {
            return;
        }

        Family family = Family.Create(owner.Id, "Test Family");

        family.AddMember(
            FamilyMember.Create(
                viewer.Id,
                owner.Id,
                FamilyRelationshipType.Son,
                FamilyRole.Viewer));

        _familiesDbContext.Families.Add(family);

        // Elderly dependents are their own aggregate root (FamiliesDbContext.Elderlies).
        _familiesDbContext.Elderlies.Add(
            Elderly.Create(
                owner.Id,
                grandfatherUser.Id,
                family.Id,
                FamilyRelationshipType.Grandfather,
                FullName.Create("الجد التجريبي"),
                FullName.Create("Test Grandfather"),
                Gender.Male,
                new DateOnly(1950, 1, 15),
                DateOnly.FromDateTime(utcNow)));

        _familiesDbContext.Elderlies.Add(
            Elderly.Create(
                owner.Id,
                grandmotherUser.Id,
                family.Id,
                FamilyRelationshipType.Grandmother,
                FullName.Create("الجدة التجريبية"),
                FullName.Create("Test Grandmother"),
                Gender.Female,
                new DateOnly(1952, 3, 20),
                DateOnly.FromDateTime(utcNow)));

        await _familiesDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<User> EnsureUserAsync(
        string arabicFullName,
        string englishFullName,
        string email,
        string phoneNumber,
        AccountType accountType,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        string normalizedEmail = Email.Create(email).Value;

        User? user = await _identityDbContext.Users
            .FirstOrDefaultAsync(
                u => u.Email != null && u.Email.Value == normalizedEmail,
                cancellationToken);

        if (user is not null)
        {
            return user;
        }

        user = User.Create(
            FullName.Create(arabicFullName),
            FullName.Create(englishFullName),
            Email.Create(email),
            PhoneNumber.Create(phoneNumber));

        user.AddAccount(accountType);
        user.SetInitialPasswordHash(
            _passwordHasher.Hash(_options.Password),
            utcNow);
        user.VerifyEmail(utcNow);
        user.VerifyPhone(utcNow);
        user.Activate(utcNow);

        _identityDbContext.Users.Add(user);
        await _identityDbContext.SaveChangesAsync(cancellationToken);

        return user;
    }

    private async Task<User> EnsureElderlyUserAsync(
        string arabicFullName,
        string englishFullName,
        string phoneNumber,
        Gender gender,
        DateOnly dateOfBirth,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        string normalizedPhoneNumber = PhoneNumber.Create(phoneNumber).Value;

        User? user = await _identityDbContext.Users
            .FirstOrDefaultAsync(
                u => u.PhoneNumber.Value == normalizedPhoneNumber,
                cancellationToken);

        if (user is not null)
        {
            return user;
        }

        // CreateElderly already sets UserStatus.Active, PhoneVerified = true,
        // DateOfBirth, Gender and the Elderly account — nothing else needed.
        user = User.CreateElderly(
            FullName.Create(arabicFullName),
            FullName.Create(englishFullName),
            PhoneNumber.Create(phoneNumber),
            gender,
            dateOfBirth,
            utcNow);

        _identityDbContext.Users.Add(user);
        await _identityDbContext.SaveChangesAsync(cancellationToken);

        return user;
    }
}
