using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.Modules.Identity.Domain.Users;

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
        return new UserAccount(
            Guid.CreateVersion7(),
            accountType,
            DateTime.UtcNow);
    }
}