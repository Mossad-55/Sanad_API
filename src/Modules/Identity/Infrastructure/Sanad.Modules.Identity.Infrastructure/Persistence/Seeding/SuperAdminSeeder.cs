using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Seeding;

public sealed class SuperAdminSeeder
{
    private const int MinimumPasswordLength = 10;
    private const int MaximumPasswordLength = 128;

    private readonly IdentityDbContext _dbContext;
    private readonly AdminSeedOptions _options;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;

    public SuperAdminSeeder(
        IdentityDbContext dbContext,
        IOptions<AdminSeedOptions> options,
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task SeedAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return;
        }

        if (await SuperAdminExistsAsync(
                cancellationToken))
        {
            return;
        }

        EnsurePasswordMeetsPolicy(
            _options.Password);

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        User user = User.Create(
            FullName.Create(
                _options.ArabicFullName),
            FullName.Create(
                _options.EnglishFullName),
            Email.Create(
                _options.Email),
            PhoneNumber.Create(
                _options.PhoneNumber));

        user.AddAccount(
            AccountType.SuperAdmin);

        user.SetInitialPasswordHash(
            _passwordHasher.Hash(
                _options.Password),
            utcNow);

        user.VerifyEmail(
            utcNow);

        user.VerifyPhone(
            utcNow);

        user.Activate(
            utcNow);

        _dbContext.Users.Add(
            user);

        await _dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private async Task<bool> SuperAdminExistsAsync(
        CancellationToken cancellationToken)
    {
        List<User> users =
            await _dbContext.Users.ToListAsync(
                cancellationToken);

        return users.Any(
            user =>
                user.Accounts.Any(
                    account =>
                        account.AccountType ==
                        AccountType.SuperAdmin));
    }

    private static void EnsurePasswordMeetsPolicy(
        string password)
    {
        if (password.Length < MinimumPasswordLength ||
            password.Length > MaximumPasswordLength ||
            !Regex.IsMatch(password, "[A-Z]") ||
            !Regex.IsMatch(password, "[a-z]") ||
            !Regex.IsMatch(password, "[0-9]"))
        {
            throw new InvalidOperationException(
                "Identity:AdminSeed:Password must be 10-128 " +
                "characters and contain uppercase, lowercase, " +
                "and a number.");
        }
    }
}