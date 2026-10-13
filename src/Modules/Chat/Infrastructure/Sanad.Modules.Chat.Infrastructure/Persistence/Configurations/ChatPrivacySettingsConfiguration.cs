using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Privacy;

namespace Sanad.Modules.Chat.Infrastructure.Persistence.Configurations;

public class ChatPrivacySettingsConfiguration : IEntityTypeConfiguration<ChatPrivacySettings>
{
    public void Configure(EntityTypeBuilder<ChatPrivacySettings> builder)
    {
        builder.ToTable("ChatPrivacySettings", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new ChatPrivacySettingsId(value));
        builder.Property(e => e.OwnerUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.ShowOnlineStatus)
            .IsRequired();
        builder.Property(e => e.ShareLastSeen)
            .IsRequired();
        builder.Property(e => e.UpdatedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Ignore(e => e.DomainEvents);

        // One settings row per owner.
        builder.HasIndex(e => e.OwnerUserId)
            .IsUnique();
    }
}
