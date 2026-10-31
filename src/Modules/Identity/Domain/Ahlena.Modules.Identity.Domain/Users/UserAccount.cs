using Ahlena.BuildingBlocks.Domain.Abstractions;

namespace Ahlena.Modules.Identity.Domain.Users;

public sealed class UserAccount : Entity<Guid>
{
    private UserAccount()
    {
    }

    private UserAccount(
        Guid id,
        AccountType accountType,
        DateTime createdOnUtc)
        : base(id)
    {
        AccountType = accountType;
        CreatedOnUtc = createdOnUtc;
    }

    public AccountType AccountType { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public static UserAccount Create(AccountType accountType)
    {
        return Create(accountType, DateTime.UtcNow);
    }

    public static UserAccount Create(AccountType accountType, DateTime createdOnUtc)
    {
        return new UserAccount(
            Guid.CreateVersion7(),
            accountType,
            createdOnUtc);
    }
}
