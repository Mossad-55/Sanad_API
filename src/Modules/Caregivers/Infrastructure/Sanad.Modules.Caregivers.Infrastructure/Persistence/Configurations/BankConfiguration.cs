using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class BankConfiguration :
    IEntityTypeConfiguration<Bank>
{
    public void Configure(EntityTypeBuilder<Bank> builder)
    {
        builder.ToTable("banks");
        builder.HasKey(bank => bank.Id);
        builder.Property(bank => bank.Id)
            .HasConversion(id => id.Value, value => new BankId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(bank => bank.Code)
            .HasColumnName("code")
            .HasMaxLength(Bank.MaximumCodeLength)
            .IsRequired();
        builder.HasIndex(bank => bank.Code).IsUnique();
        builder.Property(bank => bank.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(Bank.MaximumNameLength)
            .IsRequired();
        builder.Property(bank => bank.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(Bank.MaximumNameLength)
            .IsRequired();
        builder.Property(bank => bank.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(bank => bank.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(bank => bank.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.Ignore(bank => bank.DomainEvents);
    }
}
